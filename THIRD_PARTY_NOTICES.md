# Third-party notices

Focus's original source code is licensed under the [MIT License](LICENSE), copyright (c) 2026 AvroraCL. Third-party libraries retain their own copyright notices and licenses; Focus's license does not replace them.

## Runtime dependencies

The versions below are taken from the restored NuGet metadata for the v0.0.3 development build.

| Component | Version | License / notice | Source |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0; LibreHardwareMonitor contributors | [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) |
| Microsoft.Diagnostics.Tracing.TraceEvent | 3.2.2 | MIT; Microsoft Corporation | [PerfView](https://github.com/microsoft/perfview) |
| DiskInfoToolkit | 1.1.2 | MPL-2.0; Florian K. | [DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit) |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0; Florian K. | [RAMSPDToolkit](https://github.com/Blacktempel/RAMSPDToolkit) |
| BlackSharp.Core | 1.0.7 | MPL-2.0; Florian K. | [BlackSharp](https://github.com/Blacktempel/BlackSharp) |
| HidSharp | 2.6.4 | Apache-2.0; copyright 2010–2025 James F. Bellinger | [HidSharp](https://www.nuget.org/packages/HidSharp/2.6.4) |
| Mono.Posix.NETStandard | 1.0.0 | Microsoft Corporation; see the package's [license link](https://go.microsoft.com/fwlink/?linkid=869050) | [NuGet package](https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0) |

Microsoft.Diagnostics.NETCore.Client, Microsoft.Extensions.*, and the System.* packages resolved by the application declare MIT in their NuGet metadata. .NET runtime files included by a self-contained publish retain Microsoft's own license and third-party notices.

Focus consumes these NuGet packages without modifying their source. Hardware libraries may include additional embedded components and upstream notices; consult the exact upstream version when redistributing them. The authoritative dependency versions and license declarations are in each restored package's `.nuspec` file and `src/Focus.App/obj/project.assets.json`, not the Focus MIT file.

## License references

- [MIT](https://opensource.org/license/mit)
- [Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/)
- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

The Rust `focus-native` crate currently has no third-party crate dependencies. Development and test dependencies retain their own licenses and are not part of the application runtime package.
