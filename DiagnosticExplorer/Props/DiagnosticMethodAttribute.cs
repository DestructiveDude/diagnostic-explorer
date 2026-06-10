using System;

namespace DiagnosticExplorer.Props;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class DiagnosticMethodAttribute : Attribute
{
}