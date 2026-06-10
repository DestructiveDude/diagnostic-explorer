#region Copyright

// Diagnostic Explorer, a .Net diagnostic toolset
// Copyright (C) 2010 Cameron Elliot
// 
// This file is part of Diagnostic Explorer.
// 
// Diagnostic Explorer is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Diagnostic Explorer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public License
// along with Diagnostic Explorer.  If not, see <http://www.gnu.org/licenses/>.
// 
// http://diagexplorer.sourceforge.net/

#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using DiagnosticExplorer.Util;
using MongoDB.Bson.Serialization.Conventions;

namespace DiagnosticExplorer.Common;

public class DiagProcess
{
    public string Id { get; set; } = null!;

    public string? InstanceId { get; set; }

    public string? ProcessName { get; set; }

    public int ProcessId { get; set; }

    public string? MachineName { get; set; }

    public string? UserName { get; set; }

    public OnlineState State { get; set; }

    public RegistrationMode RegistrationMode { get; set; } = RegistrationMode.NA;

    public string? Message { get; set; }
    public int AlertLevel { get; set; } = 0;


    [JsonIgnore] public DateTime? AlertLevelDate { get; set; } = null;

    public DateTime? LastOnline { get; set; }

    [Newtonsoft.Json.JsonIgnore]
    [JsonIgnore]
    public string? ConnectionId { get; set; }


    public DiagProcess Clone()
    {
        return (DiagProcess) MemberwiseClone();
    }

    public override string ToString()
    {
        return
            $"DiagProcess PID:{ProcessId} Process:{ProcessName}, State:{State}, LastOnline:{LastOnline}";
    }
}

public static class DiagProcessExtensions
{
    public static DiagProcess? FindById(this IEnumerable<DiagProcess> list, string id)
    {
        return list.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.InvariantCultureIgnoreCase));
    }

    public static DiagProcess? FindByConnectionId(this IEnumerable<DiagProcess> list, string connectionId)
    {
        return list.FirstOrDefault(item => string.Equals(item.ConnectionId, connectionId, StringComparison.InvariantCultureIgnoreCase));
    }

    public static DiagProcess? FindByInstanceId(this IEnumerable<DiagProcess> list, string instanceId)
    {
        return list.FirstOrDefault(item => string.Equals(item.InstanceId, instanceId, StringComparison.InvariantCultureIgnoreCase));
    }

}