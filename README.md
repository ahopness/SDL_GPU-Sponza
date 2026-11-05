Updated branch with the following optimizations:
- Cull meshes against each cascade’s light-space volume. Currently every visible shadow caster is drawn into all four cascades.
- Add camera-frustum culling. Currently Visible meshes are submitted even when outside the camera view.
- Replace the per-frame SelectMany(...).Distinct() scan with preparation when materials are loaded or changed.
- Avoid allocating new[] for vertex buffers and sampler bindings on every draw.
- Cache inverse-transpose normal matrices until an instance’s transform changes.
- Cache decoded mesh geometry by glTF mesh index. Multiple nodes currently rebuild the same geometry.

---

# SDL_GPU: Sponza Demo

<img width="1590" height="891" alt="Screenshot From 2026-11-05 18-18-23" src="https://github.com/user-attachments/assets/b3ee2152-4ab1-4cfb-834d-3003cc286def" />

C# deferred renderer and cascaded shadow maps using the SDL_GPU API. 

**Requires .NET 10 and the SDL_shadercross CLI on PATH.**

DEMO CONTROLS: Drag left to orbit, right or middle to pan, and scroll to zoom.

---

I’ve tried to keep the code as simple as possible to make it easier to understand, though this comes at the cost of performance in some areas of rendering, so please keep that in mind.

I had some help from GPT-6.1 Sol in a few areas, mostly with parsers and in the implementation of certain algorithms. If anything seems confusing or redundant, that’s on me for not paying close enough attention. Any PRs aimed at simplifying the code are more than welcome.
