﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.Common;
using TAS.Server.Common;
using TAS.Server.Interfaces;

namespace TAS.Remoting.Model
{
    public class AnimatedMedia : PersistentMedia, IAnimatedMedia
    {
        public IDictionary<string, string> Fields { get { return Get<Dictionary<string, string>>(); } set { Set(value); } }

        public TemplateMethod Method { get { return Get<TemplateMethod>(); } set { Set(value); } }

        public int TemplateLayer { get { return Get<int>(); } set { Set(value); } }

    }
}
