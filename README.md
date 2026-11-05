# SDL_GPU-Sponza

C# deferred renderer using SDL3's GPU API: G-buffer, directional lighting, and four cascaded shadow maps. Requires .NET 10 and the SDL_shadercross `shadercross` CLI on PATH. The build compiles the HLSL shaders to SPIR-V, MSL, and DXIL; runtime selects a format supported by the SDL GPU device.

```sh
dotnet build
dotnet run --no-build -- sponza
dotnet run --no-build -- cornell-box
```

Drag left to orbit, right or middle to pan, and scroll to zoom. Render targets follow the window's pixel size. Close the window to quit.

`dotnet run --no-build -- sponza --smoke-test` uploads blue noise and HDR mipmaps, loads the scene and materials, renders three frames, waits for the GPU, and exits. Use `cornell-box` to exercise the single-shadow projection, or `beast` for the second bundled model.

All core classes live in `SponzaTest.Core`. `SdlGpuContext` owns the GPU device/window claim; GPU buffers and textures upload through SDL transfer buffers and copy passes. `SceneRenderer` records shadow, G-buffer, and composite passes and presents by submitting the command buffer. Dispose scenes, asset loaders, and renderers before disposing the GPU context. Environment textures belong to the loader cache; a scene's environment reference is borrowed.

The Sponza preset loads the uncompressed `Resources/Models/sponza.glb` directly. Other legacy presets (`leonardo`, `occlusion`, `marble-bust`) require their model files under `Resources/Models`. EXR reading retains the native OpenEXR dependency and procedural fallback. HDR and glTF decoding remain CPU operations. Legacy GI settings are retained as scene configuration; the composite shader combines direct lighting, CSM, and diffuse HDR environment lighting.

Image decoding uses ImageSharp 4.1.3. Its build targets report that a valid Six Labors license is required for Release builds; configure that dependency's license before building Release.

The HDR environment illuminates surfaces using nine spherical harmonic coefficients computed at scene initialization. Set `light.AmbientIntensity` in the preset to scale this lighting (default `1`, `0` disables it) without changing skybox brightness. This is diffuse environment lighting without scene occlusion or specular environment reflections.
