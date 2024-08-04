/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System.Globalization;
using System.IO;
using System.Text;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    partial class UnityCsProjectConverter
    {
        // NOTE: resulting .csproj file is written by Unity, not by this converter, and file encoding is utf-8.
        //       this writer class is required due to XDocument automatically update <?xml encoding="..."?> to writer's encoding. (ex: utf-16)
        sealed class XDocumentWriter : StringWriter
        {
            public XDocumentWriter(StringBuilder sb) : base(sb, CultureInfo.InvariantCulture) { }
            public override Encoding Encoding => Encoding.UTF8;
        }

    }
}
