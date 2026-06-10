using System;

namespace DiagnosticExplorer;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class DiagnosticMethodAttribute : Attribute
{
}