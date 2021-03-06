﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.IO;
using System.Diagnostics;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.Remoting.Messaging;
using System.Globalization;
using System.Text;
using System.ComponentModel;
using TAS.FFMpegUtils;
using TAS.Common;
using TAS.Server.Interfaces;
using Newtonsoft.Json;
using System.Drawing;
using System.Drawing.Imaging;
using TAS.Server.Common;

namespace TAS.Server
{
    public class ConvertOperation : FFMpegOperation, IConvertOperation
    {
        
        #region Properties

        public ConvertOperation()
        {
            Kind = TFileOperationKind.Convert;
            _aspectConversion = TAspectConversion.NoConversion;
            _sourceFieldOrderEnforceConversion = TFieldOrder.Unknown;
            _audioChannelMappingConversion = TAudioChannelMappingConversion.FirstTwoChannels;
        }

        #endregion // properties


        #region IConvertOperation implementation

        private TAspectConversion _aspectConversion;
        private TAudioChannelMappingConversion _audioChannelMappingConversion;
        private decimal _audioVolume;
        private TFieldOrder _sourceFieldOrderEnforceConversion;

        [JsonProperty]
        public TAspectConversion AspectConversion { get { return _aspectConversion; } set { SetField(ref _aspectConversion, value); } }
        [JsonProperty]
        public TAudioChannelMappingConversion AudioChannelMappingConversion { get { return _audioChannelMappingConversion; } set { SetField(ref _audioChannelMappingConversion, value); } }
        [JsonProperty]
        public decimal AudioVolume { get { return _audioVolume; } set { SetField(ref _audioVolume, value); } }
        [JsonProperty]
        public TFieldOrder SourceFieldOrderEnforceConversion { get { return _sourceFieldOrderEnforceConversion; } set { SetField(ref _sourceFieldOrderEnforceConversion, value); } }
        [JsonProperty]
        public TimeSpan StartTC { get; set; }
        [JsonProperty]
        public TimeSpan Duration { get; set; }
        [JsonProperty]
        public bool Trim { get; set; }

        [JsonProperty]
        public bool LoudnessCheck { get; set; }


        #endregion // IConvertOperation implementation


        public override bool Do()
        {
            if (Kind == TFileOperationKind.Convert)
            {
                StartTime = DateTime.UtcNow;
                OperationStatus = FileOperationStatus.InProgress;
                IsIndeterminate = true;
                try
                {
                    Media sourceMedia = SourceMedia as IngestMedia;
                    if (sourceMedia == null)
                        throw new ArgumentException("ConvertOperation: SourceMedia is not of type IngestMedia");
                    bool success = false;
                    if (((IngestDirectory)sourceMedia.Directory).AccessType != TDirectoryAccessType.Direct)
                        using (TempMedia _localSourceMedia = (TempMedia)Owner.TempDirectory.CreateMedia(sourceMedia))
                        {
                            AddOutputMessage($"Copying to local file {_localSourceMedia.FullPath}");
                            _localSourceMedia.PropertyChanged += _localSourceMedia_PropertyChanged;
                            if (sourceMedia.CopyMediaTo(_localSourceMedia, ref _aborted))
                            {
                                AddOutputMessage("Verifing local file");
                                _localSourceMedia.Verify();
                                try
                                {
                                    if (DestMediaProperties.MediaType == TMediaType.Still)
                                        success = _convertStill(_localSourceMedia);
                                    else
                                        success = _convertMovie(_localSourceMedia, _localSourceMedia.StreamInfo);
                                }
                                finally
                                {
                                    _localSourceMedia.PropertyChanged -= _localSourceMedia_PropertyChanged;
                                }

                                if (!success)
                                    TryCount--;
                                return success;
                            }
                            return false;
                        }

                    else
                    {
                        if (sourceMedia is IngestMedia && sourceMedia.IsVerified)
                        {
                            if (DestMediaProperties.MediaType == TMediaType.Still)
                                success = _convertStill(sourceMedia);
                            else
                                success = _convertMovie(sourceMedia, ((IngestMedia)sourceMedia).StreamInfo);
                            if (!success)
                                TryCount--;
                        }
                        else
                            AddOutputMessage("Waiting for media to verify");
                        return success;
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    AddOutputMessage(e.Message);
                    TryCount--;
                    return false;
                }

            }
            else
                return base.Do();
        }

        void _localSourceMedia_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IMedia.FileSize))
            {
                ulong fs = SourceMedia.FileSize;
                if (fs > 0 && sender is Media)
                    Progress = (int)(((sender as Media).FileSize * 100ul) / fs);
            }
        }

        private bool _convertStill(Media localSourceMedia)
        {
            CreateDestMediaIfNotExists();
            Media destMedia = DestMedia as Media;
            if (destMedia != null)
            {
                destMedia.MediaType = TMediaType.Still;
                Size destSize = destMedia.VideoFormat == TVideoFormat.Other ? VideoFormatDescription.Descriptions[TVideoFormat.HD1080i5000].ImageSize : destMedia.FormatDescription().ImageSize;
                Image bmp = new Bitmap(destSize.Width, destSize.Height, PixelFormat.Format32bppArgb);
                Graphics graphics = Graphics.FromImage(bmp);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                if (Path.GetExtension(localSourceMedia.FileName).ToLowerInvariant() == ".tga")
                {
                    var tgaImage = new Paloma.TargaImage(localSourceMedia.FullPath);
                    graphics.DrawImage(tgaImage.Image, 0, 0, destSize.Width, destSize.Height);
                }
                else
                    graphics.DrawImage(new Bitmap(localSourceMedia.FullPath), 0, 0, destSize.Width, destSize.Height);
                ImageCodecInfo imageCodecInfo = ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.FilenameExtension.Split(';').Select(se => se.Trim('*')).Contains(FileUtils.DefaultFileExtension(TMediaType.Still).ToUpperInvariant()));
                System.Drawing.Imaging.Encoder encoder = System.Drawing.Imaging.Encoder.Quality;
                EncoderParameter encoderParameter = new EncoderParameter(encoder, 90L);
                EncoderParameters encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = encoderParameter;
                bmp.Save(destMedia.FullPath, imageCodecInfo, encoderParameters);
                destMedia.MediaStatus = TMediaStatus.Copied;
                ((Media)destMedia).Verify();
                OperationStatus = FileOperationStatus.Finished;
                return true;
            }
            else return false;
        }

        #region Movie conversion
        private void _addConversion(MediaConversion conversion, List<string> filters)
        {
            if (conversion != null)
            {
                if (!string.IsNullOrWhiteSpace(conversion.FFMpegFilter))
                    filters.Add(conversion.FFMpegFilter);
            }
        }

        private string _encodeParameters(Media inputMedia, StreamInfo[] inputStreams)
        {
            List<string> video_filters = new List<string>();
            StringBuilder ep = new StringBuilder();
            var sourceDir = SourceMedia.Directory as IngestDirectory;
            #region Video
            ep.AppendFormat(" -c:v {0}", sourceDir.VideoCodec);
            if (sourceDir.VideoCodec == TVideoCodec.copy)
            {
                if (AspectConversion == TAspectConversion.Force16_9)
                    ep.Append(" -aspect 16/9");
                else
                if (AspectConversion == TAspectConversion.Force4_3)
                    ep.Append(" -aspect 4/3");
            }
            else
            {
                ep.AppendFormat(" -b:v {0}k", (int)(inputMedia.FormatDescription().ImageSize.Height * 13 * (double)sourceDir.VideoBitrateRatio));
                VideoFormatDescription outputFormatDescription = DestMedia.FormatDescription();
                VideoFormatDescription inputFormatDescription = inputMedia.FormatDescription();
                _addConversion(MediaConversion.SourceFieldOrderEnforceConversions[SourceFieldOrderEnforceConversion], video_filters);
                if (inputMedia.HasExtraLines)
                {
                    video_filters.Add("crop=720:576:0:32");
                    if (AspectConversion == TAspectConversion.NoConversion)
                    {
                        if (inputFormatDescription.IsWideScreen)
                            video_filters.Add("setdar=dar=16/9");
                        else
                            video_filters.Add("setdar=dar=4/3");
                    }
                }
                if (AspectConversion == TAspectConversion.NoConversion)
                {
                    if (inputFormatDescription.IsWideScreen)
                        video_filters.Add("setdar=dar=16/9");
                    else
                        video_filters.Add("setdar=dar=4/3");
                }
                if (AspectConversion != TAspectConversion.NoConversion)
                    _addConversion(MediaConversion.AspectConversions[AspectConversion], video_filters);
                if (inputFormatDescription.FrameRate / outputFormatDescription.FrameRate == 2 && outputFormatDescription.Interlaced)
                    video_filters.Add("tinterlace=interleave_top");
                video_filters.Add($"fps=fps={outputFormatDescription.FrameRate}");
                if (outputFormatDescription.Interlaced)
                {
                    video_filters.Add("fieldorder=tff");
                    ep.Append(" -flags +ildct+ilme");
                }
                else
                {
                    video_filters.Add("w3fdif");
                }
                var additionalEncodeParams = ((IngestDirectory)SourceMedia.Directory).EncodeParams;
                if (!string.IsNullOrWhiteSpace(additionalEncodeParams))
                    ep.Append(" ").Append(additionalEncodeParams.Trim());
            }
            int lastFilterIndex = video_filters.Count() - 1;
            if (lastFilterIndex >= 0)
            {
                video_filters[lastFilterIndex] = $"{video_filters[lastFilterIndex]}[v]";
                ep.Append(" -map \"[v]\"");
            } else
            {
                var videoStream = inputStreams.FirstOrDefault(s => s.StreamType == StreamType.VIDEO);
                if (videoStream != null)
                    ep.AppendFormat(" -map 0:{0}", videoStream.Index);
            }
            #endregion // Video

            #region Audio
            List<string> audio_filters = new List<string>();
            StreamInfo[] audioStreams = inputStreams.Where(s => s.StreamType == StreamType.AUDIO).ToArray();
            if (audioStreams.Length > 0)
            {
                ep.AppendFormat(" -c:a {0}", sourceDir.AudioCodec);
                if (sourceDir.AudioCodec != TAudioCodec.copy)
                {
                    ep.AppendFormat(" -b:a {0}k", (int)(2 * 128 * sourceDir.AudioBitrateRatio));
                    MediaConversion audiChannelMappingConversion = MediaConversion.AudioChannelMapingConversions[AudioChannelMappingConversion];
                    int inputTotalChannels = audioStreams.Sum(s => s.ChannelCount);
                    int requiredOutputChannels;
                    switch ((TAudioChannelMappingConversion)audiChannelMappingConversion.OutputFormat)
                    {
                        case TAudioChannelMappingConversion.FirstTwoChannels:
                        case TAudioChannelMappingConversion.SecondChannelOnly:
                        case TAudioChannelMappingConversion.Combine1plus2:
                            requiredOutputChannels = 2;
                            break;
                        case TAudioChannelMappingConversion.SecondTwoChannels:
                        case TAudioChannelMappingConversion.Combine3plus4:
                            requiredOutputChannels = 4;
                            break;
                        case TAudioChannelMappingConversion.FirstChannelOnly:
                            requiredOutputChannels = 1;
                            break;
                        default:
                            requiredOutputChannels = 0;
                            break;
                    }
                    if (audioStreams.Length > 1 && requiredOutputChannels > audioStreams[0].ChannelCount)
                    {
                        int audio_stream_count = 0;
                        StringBuilder pf = new StringBuilder();
                        foreach (StreamInfo stream in audioStreams)
                        {
                            pf.AppendFormat("[0:{0}]", stream.Index);
                            audio_stream_count += stream.ChannelCount;
                        }
                        audio_filters.Add(string.Format("{0}amerge=inputs={1}", pf.ToString(), audioStreams.Length));
                    }
                    _addConversion(audiChannelMappingConversion, audio_filters);
                    if (AudioVolume != 0)
                        _addConversion(new MediaConversion(AudioVolume), audio_filters);
                    ep.Append(" -ar 48000");
                }
            }
            lastFilterIndex = audio_filters.Count() - 1;
            if (lastFilterIndex >= 0)
            {
                audio_filters[lastFilterIndex] = $"{audio_filters[lastFilterIndex]}[a]";
                ep.Append(" -map \"[a]\"");
            }
            else
            {
                var audioStream = inputStreams.FirstOrDefault(s => s.StreamType == StreamType.AUDIO);
                if (audioStream != null)
                    ep.AppendFormat(" -map 0:{0}", audioStream.Index);
            }
            #endregion // audio
            var filters = video_filters.Concat(audio_filters);
            if (filters.Any())
                ep.AppendFormat(" -filter_complex \"{0}\"", string.Join(",", filters));
            return ep.ToString();
        }

        private bool _is_trimmed()
        {
            return Trim && Duration > TimeSpan.Zero && ((IngestDirectory)SourceMedia.Directory).VideoCodec != TVideoCodec.copy ;
        }

        private bool _convertMovie(Media localSourceMedia, StreamInfo[] streams)
        {
            if (!localSourceMedia.FileExists() || streams == null)
            {
                Debug.WriteLine(this, "Cannot start conversion: file not readed");
                AddOutputMessage("Cannot start conversion: file not readed");
                return false;
            }
            CreateDestMediaIfNotExists();
            Media destMedia = DestMedia as Media;
            if (destMedia != null)
            {
                _progressDuration = localSourceMedia.Duration;
                Debug.WriteLine(this, "Convert operation started");
                AddOutputMessage("Starting convert operation:");
                destMedia.MediaStatus = TMediaStatus.Copying;
                //CheckInputFile(media);
                string encodeParams = _encodeParameters(localSourceMedia, streams);
                //TimeSpan outStartTC = _is_trimmed() ? 
                string ingestRegion = _is_trimmed() ?
                    string.Format(System.Globalization.CultureInfo.InvariantCulture, " -ss {0} -t {1}", StartTC - SourceMedia.TcStart, Duration) : string.Empty;
                string Params = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        " -i \"{1}\"{0} -vsync cfr{2} -timecode {3} -y \"{4}\"",
                        ingestRegion,
                        localSourceMedia.FullPath,
                        encodeParams,
                        StartTC.ToSMPTETimecodeString(destMedia.FrameRate()),
                    destMedia.FullPath);
            if (DestMedia is ArchiveMedia)
                FileUtils.CreateDirectoryIfNotExists(Path.GetDirectoryName(destMedia.FullPath));
            DestMedia.AudioChannelMapping = (TAudioChannelMapping)MediaConversion.AudioChannelMapingConversions[AudioChannelMappingConversion].OutputFormat;
                if (RunProcess(Params)  // FFmpeg 
                    && destMedia.FileExists())
                {
                    destMedia.MediaStatus = TMediaStatus.Copied;
                    ((Media)destMedia).Verify();
                    if (Math.Abs(destMedia.Duration.Ticks - (_is_trimmed() ? Duration.Ticks : localSourceMedia.Duration.Ticks)) > TimeSpan.TicksPerSecond / 2)
                    {
                        destMedia.MediaStatus = TMediaStatus.CopyError;
                        if (destMedia is PersistentMedia)
                            (destMedia as PersistentMedia).Save();
                        _addWarningMessage($"Durations are different: {localSourceMedia.Duration.ToSMPTETimecodeString(localSourceMedia.FrameRate())} vs {destMedia.Duration.ToSMPTETimecodeString(destMedia.FrameRate())}");
                        Debug.WriteLine(this, "Convert operation succeed, but durations are diffrent");
                    }
                    else
                    {
                        if ((SourceMedia.Directory is IngestDirectory) && ((IngestDirectory)SourceMedia.Directory).DeleteSource)
                            ThreadPool.QueueUserWorkItem((o) =>
                            {
                                Thread.Sleep(2000);
                                Owner.Queue(new FileOperation { Kind = TFileOperationKind.Delete, SourceMedia = SourceMedia });
                            });
                        AddOutputMessage("Convert operation finished successfully");
                        Debug.WriteLine(this, "Convert operation succeed");
                    }
                    OperationStatus = FileOperationStatus.Finished;
                    if (LoudnessCheck)
                    {
                        ThreadPool.QueueUserWorkItem((o) =>
                        {
                            Thread.Sleep(2000);
                            Owner.Queue(new LoudnessOperation() { SourceMedia = destMedia });
                        });
                    }
                    return true;
                }
                Debug.WriteLine("FFmpeg rewraper Do(): Failed for {0}. Command line was {1}", (object)SourceMedia, Params);
            }
            return false;
        }
        #endregion //Movie conversion

        protected override void ProcOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            base.ProcOutputHandler(sendingProcess, outLine);
            if (!string.IsNullOrEmpty(outLine.Data) 
                && outLine.Data.Contains("error")) 
                _addWarningMessage($"FFmpeg error: {outLine.Data}");
        }

    }

}
 