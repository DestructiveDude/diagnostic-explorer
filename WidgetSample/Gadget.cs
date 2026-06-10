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
using System.ComponentModel;
using DiagnosticExplorer;
using DiagnosticExplorer.Props;

namespace WidgetSample;

//Widget extends DiagnosticManager in order to register itself with diagnostics
public class Gadget : IDisposable, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public Gadget(int id)
    {
        Id = id;

        Name = GetRandom(_names);
        Purpose = GetRandom(_purposes);
        DiagnosticManager.Register(this, string.Format("Gadget {0}", Id), "Gadgets");
    }

    [DiagnosticMethod]
    public void Randomise()
    {
        Name = GetRandom(_names);
        Purpose = GetRandom(_purposes);
    }

    [DiagnosticMethod]
    public void Clear()
    {
        Name = null;
        Purpose = null;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region IDisposable Members

    // Mirror Widget: dispose unregisters the gadget from diagnostics so removal is
    // deterministic (via RemoveItem) instead of relying on a GC pass to drop the
    // DiagnosticManager registration.
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Gadget()
    {
        Dispose(false);
    }

    protected void Dispose(bool disposing)
    {
        if (disposing)
        {
            DiagnosticManager.Unregister(this);
        }
    }

    #endregion

    private string GetRandom(string[] items)
    {
        int index = ThreadSafeRandom.Next(0, items.Length);
        return items[index];
    }

    private static readonly string[] _names = new[]
                                         {"Gadget X", "Gadget Y", "Gadget Z", "Gadget W"};

    private static readonly string[] _purposes = new[]
                                            {"Technical", "Muckabout", "Stuff"};

    public override string ToString()
    {
        return string.Format("Gadget {0}", Id);
    }

    public int Id { get; private set; }

    private string _name;

    [Property(AllowSet = true)]
    public string Name
    {
        get => _name;
        set {
            _name = value;
            OnPropertyChanged("Name");
        }
    }

    private string _purpose;

    [Property(AllowSet = true)]
    public string Purpose
    {
        get => _purpose;
        set {
            _purpose = value;
            OnPropertyChanged("Purpose");
        }
    }
}