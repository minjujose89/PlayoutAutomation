﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace TAS.Server.Interfaces
{
    public interface IPersistent
    {
        ulong Id { get; set; }
    }
}
