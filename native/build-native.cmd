@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
set "SDKHEAD=%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.cpp\10.0.26100.1\c\Include\10.0.26100.0"
set "SDKLIB=%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.cpp.x64\10.0.26100.1\c"
set "INCLUDE=%INCLUDE%;%SDKHEAD%\um;%SDKHEAD%\shared;%SDKHEAD%\ucrt;%SDKHEAD%\winrt"
set "LIB=%LIB%;%SDKLIB%\um\x64;%SDKLIB%\ucrt\x64"
if not exist build mkdir build
if not exist ..\dist\SinkingStarInGame mkdir ..\dist\SinkingStarInGame
cl /nologo /std:c++17 /utf-8 /EHsc /O2 /MT /W3 /LD /DUNICODE /D_UNICODE /DNOMINMAX /Ivendor\imgui-src /Ivendor\imgui-src\backends overlay.cpp vendor\imgui-src\imgui.cpp vendor\imgui-src\imgui_draw.cpp vendor\imgui-src\imgui_tables.cpp vendor\imgui-src\imgui_widgets.cpp vendor\imgui-src\backends\imgui_impl_win32.cpp vendor\imgui-src\backends\imgui_impl_dx12.cpp /Fobuild\ /link /DEF:dxgi.def /OUT:..\dist\SinkingStarInGame\dxgi.dll user32.lib gdi32.lib d3d12.lib d3dcompiler.lib dwmapi.lib
exit /b %errorlevel%
