@echo off

:: ADAPTED FROM https://github.com/TheSpydog/SDL_gpu_examples/blob/main/Content/Shaders/Source/compile.bat
:: Requires shadercross CLI installed from SDL_shadercross
pushd "%~dp0"
if errorlevel 1 exit /b 1

set "output_dir=%~1"
if not defined output_dir set "output_dir=Compiled"
if not exist "%output_dir%\" mkdir "%output_dir%"
if not exist "%output_dir%\" goto failed

set "found="
for %%f in (*.vert.hlsl *.frag.hlsl *.comp.hlsl) do (
    if exist "%%f" (
        set "found=1"
        call :shader "%%f"
        if errorlevel 1 goto failed
    )
)
if not defined found (
    echo No *.vert.hlsl, *.frag.hlsl, or *.comp.hlsl shaders found. 1>&2
    goto failed
)
popd
exit /b 0

:shader
for %%x in (spv msl dxil) do (
    echo Compiling %~1 -^> %output_dir%\%~n1.%%x
    shadercross "%~1" -s HLSL -e main -o "%output_dir%\%~n1.%%x"
    if errorlevel 1 exit /b 1
)
exit /b 0

:failed
popd
exit /b 1
