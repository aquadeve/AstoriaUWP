# Android bridge for UWP applications
Allows run Android Runtime as an UWP app.

[![Build status](https://ci.appveyor.com/api/projects/status/h0a9b5qfy3rq4amf/branch/master?svg=true)](https://ci.appveyor.com/project/WallyCZ/bridge/branch/master)

[![Join the chat at https://gitter.im/DroidOnUWP/Bridge](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/DroidOnUWP/Bridge?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)


# Status
- ARMv7 and x86 runtime payloads are wired into the UWP package
- x64 (x86_64 / WindowsRT) build configurations added; requires a packaged `Android\lib\x64` runtime payload from the AndroidLibs submodule
- fork() uses a UWP-compatible CreateProcess-based implementation; RtlCloneUserProcess is not permitted in the AppContainer sandbox
- Android Runtime launch runs on a dedicated thread pool thread and is protected by a structured-exception handler so that a crash in the native runtime does not bring down the UWP host process
- Android 7.1.1 r13 is used
- Visual Studio 2015 (toolset v140, Windows SDK 10.0.14393.0) supported

# Build
1. Clone repo including submodules: `git clone --recurse-submodules --depth=1 https://github.com/aquadeve/Bridge`
2. Install Angle templates (run `Angle\templates\install.bat`) and build the Angle solution (`Angle\winrt\10\gyp\All.vcxproj`) separately
3. Open Bridge.sln
4. Choose target platform: ARM, x86, or x64
5. Build

> **Note:** When debugging in Visual Studio, access-violation exceptions will be raised by the FLinux layer during normal operation.
> Uncheck "Break when this exception type is thrown" for access violations so that the FLinux exception handler can work correctly.

# FAQ
Q: When debugging in Visual Studio Access violation exception occurs. How to fix it?
A: Uncheck "Break when this exception type is thrown", so FLinux exception handler can work correctly
