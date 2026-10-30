# Third-party notices

Udjat itself is under the MIT license (see `LICENSE`). It uses the open-source components below, each under its own license.
Complete license texts are in the `licenses` folder next to this file, or at the links given.

| Component | Version | License | Copyright | Source |
|---|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | LibreHardwareMonitor contributors | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| PawnIO modules (inside LibreHardwareMonitorLib) | as in 0.9.6 | LGPL-2.1 | namazso and contributors | https://github.com/namazso/PawnIO.Modules |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | Florian K. | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | Florian K. | https://github.com/Blacktempel/RAMSPDToolkit |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | Florian K. | https://github.com/Blacktempel/BlackSharp |
| HidSharp | 2.6.4 | Apache-2.0 | James F. Bellinger | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | Microsoft Corporation | https://github.com/mono/mono |
| MailKit | 4.18.2 | MIT | .NET Foundation and Contributors (Jeffrey Stedfast) | https://github.com/jstedfast/MailKit |
| MimeKit | 4.18.2 | MIT | .NET Foundation and Contributors (Jeffrey Stedfast) | https://github.com/jstedfast/MimeKit |
| BouncyCastle.Cryptography | 2.7.0 | MIT | The Legion of the Bouncy Castle Inc. | https://github.com/bcgit/bc-csharp |
| .NET runtime, WPF and Windows Forms | 10.0 | MIT | .NET Foundation, Microsoft Corporation and contributors | https://github.com/dotnet |

## Mozilla Public License 2.0 (MPL-2.0)

LibreHardwareMonitorLib, DiskInfoToolkit, RAMSPDToolkit-NDD and BlackSharp.Core are under the Mozilla Public License, version 2.0.
The license text is at https://www.mozilla.org/MPL/2.0/.

Udjat includes these libraries **unmodified**, each as a separate `.dll` file. Their source code is available from the
addresses in the table above. For LibreHardwareMonitorLib 0.9.6 it is the commit `3d331e3370efb858411f19511373eff65a218701`
of https://github.com/LibreHardwareMonitor/LibreHardwareMonitor. Udjat's own source files are not covered by the MPL.

## GNU Lesser General Public License 2.1 (LGPL-2.1): the PawnIO modules

LibreHardwareMonitorLib contains twelve small programs, the "PawnIO modules", that the PawnIO driver runs to read some sensors
(such as the CPU temperature). They come from https://github.com/namazso/PawnIO.Modules and are under the LGPL, version 2.1
(the text is in `licenses/LGPL-2.1.txt`).

- The modules are embedded in `LibreHardwareMonitorLib.dll`, which Udjat ships **unmodified and as a separate file**. You can
  replace that file with another build of the same library to change or update the modules.
- The PawnIO **driver** is not part of Udjat. It is a separate program that you install yourself, from https://pawnio.eu, under
  its own terms. Udjat works without it; only the CPU temperature needs it.

## Apache License 2.0: HidSharp

HidSharp is Copyright 2010-2025 James F. Bellinger, licensed under the Apache License, Version 2.0. The full text is in
`licenses/Apache-2.0-HidSharp.txt`.

## MIT licenses

The MIT-licensed components above are distributed under the following terms, each with its own copyright line from the table:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
> files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
> modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software
> is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
> HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The .NET runtime's own license and the notices for the third-party code inside it are in `licenses/dotnet-LICENSE.txt`,
`licenses/dotnet-desktop-LICENSE.txt` and `licenses/dotnet-THIRD-PARTY-NOTICES.txt`.

## What is not shipped

The test libraries (xunit and the Microsoft test tools) are used only to run Udjat's tests and are not part of what you install.
