using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Impulse Finder")]
[assembly: AssemblyDescription("Impulse Finder Indicator for cTrader")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyProduct("Impulse Finder")]
[assembly: AssemblyTrademark("Mikhail Berdnikov (C) 2024")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: Guid("35f19c43-c7cc-4f53-b8e0-61caaf9cd8e8")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#if DEBUG
    [assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.DisableOptimizations)]
#endif