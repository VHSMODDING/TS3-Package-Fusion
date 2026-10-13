using System.Drawing;
using System;
using System.IO;

if (args.Length != 2) throw new ArgumentException("Usage: IconExtractor <executable> <output.ico>");
using var icon = Icon.ExtractAssociatedIcon(args[0]) ?? throw new InvalidOperationException("Aucune icône trouvée.");
using var output = File.Create(args[1]);
icon.Save(output);
