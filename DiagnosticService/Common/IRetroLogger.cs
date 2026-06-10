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

using Diagnostic.Service.Transport;
using DiagnosticExplorer;

namespace Diagnostic.Service.Common;

public interface IRetroLogger
{
    IAsyncEnumerable<RetroMsg[]> GetMessages(RetroQuery query, CancellationToken cancel);

    Task WriteMessages(ICollection<DiagnosticMsg> msg, CancellationToken cancel);

    Task<long> Delete(string[] idList);

    /// <summary>
    /// Whether the backing store supports interactive per-record delete. False for append-only
    /// stores (Log Analytics), letting callers gate the Retro delete affordance.
    /// </summary>
    bool SupportsDelete { get; }
}