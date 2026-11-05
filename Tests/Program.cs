using System.Numerics;
using System.Text;
using System.Text.Json;
using SponzaTest.Assets;
using SponzaTest.Graphics;
using SponzaTest.Scene;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var box = new BoundingBox(new(-0.5f), new(0.5f));
var projection = Camera.CreatePerspectiveFieldOfViewZeroToOne(MathF.PI / 2, 1, 0.1f, 100);
Check(box.IntersectsClipVolume(Matrix4x4.CreateTranslation(0, 0, -5) * projection), "Visible box culled");
Check(!box.IntersectsClipVolume(Matrix4x4.CreateTranslation(0, 0, 5) * projection), "Behind-camera box retained");
Check(box.IntersectsClipVolume(Matrix4x4.CreateTranslation(0, 0, -0.1f) * projection), "Near-plane crossing culled");
Check(!box.IntersectsClipVolume(Matrix4x4.CreateTranslation(0, 0, -101) * projection), "Beyond-far box retained");
Check(box.IntersectsClipVolume(Matrix4x4.CreateTranslation(1.5f, 0, 0)), "Boundary box culled");

// A caster outside the camera remains visible in a light volume covering it.
var offCamera = Matrix4x4.CreateTranslation(20, 0, -5);
var lightProjection = Camera.CreateOrthographicOffCenterZeroToOne(-30, 30, -30, 30, 0.1f, 100);
Check(!box.IntersectsClipVolume(offCamera * projection), "Expected off-camera caster");
Check(box.IntersectsClipVolume(offCamera * lightProjection), "Off-camera shadow caster culled");
Check(!box.IntersectsClipVolume(Matrix4x4.CreateTranslation(40, 0, -5) * lightProjection), "Outside-cascade caster retained");

// Independent brute-force clip-space corner checks validate the plane extraction.
var random = new Random(7);
for (int sample = 0; sample < 2000; sample++)
{
    var world = Matrix4x4.CreateScale(0.1f + random.NextSingle() * 4, -2, 3)
        * Matrix4x4.CreateRotationY(random.NextSingle() * 6)
        * Matrix4x4.CreateTranslation(random.NextSingle() * 80 - 40, random.NextSingle() * 30 - 15, random.NextSingle() * 120 - 110);
    var transform = world * (sample % 2 == 0 ? projection : lightProjection);
    var corners = Enumerable.Range(0, 8).Select(i => Vector4.Transform(new Vector4(box.GetCorner(i), 1), transform)).ToArray();
    bool outside = corners.All(c => c.X < -c.W) || corners.All(c => c.X > c.W)
        || corners.All(c => c.Y < -c.W) || corners.All(c => c.Y > c.W)
        || corners.All(c => c.Z < 0) || corners.All(c => c.Z > c.W);
    Check(box.IntersectsClipVolume(transform) == !outside, "Clip-plane/corner disagreement");
}

var mesh = new CpuMesh { Vertices = [new(new(-1), Vector3.UnitY, Vector2.Zero), new(new(1), Vector3.UnitY, Vector2.Zero)] };
Check(mesh.Bounds.Max == Vector3.One, "Initial bounds");
mesh.Vertices = [new(new(4), Vector3.UnitY, Vector2.Zero)];
Check(mesh.Bounds.Min == new Vector3(4), "Replacement bounds not invalidated");
mesh.Vertices[0].Position = new(5);
mesh.InvalidateBounds();
Check(mesh.Bounds.Max == new Vector3(5), "In-place bounds invalidation failed");
var instance = new SceneMeshInstance { Mesh = mesh };
Check(instance.NormalMatrix == Matrix4x4.Identity, "Identity normal matrix");
instance.WorldMatrix = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(0.7f);
Matrix4x4.Invert(instance.WorldMatrix, out var inverse);
Check(instance.NormalMatrix == Matrix4x4.Transpose(inverse), "Transform did not invalidate normal matrix");

var materials = new VersionedCollection<MaterialData>();
long revision = VersionedCollection<MaterialData>.Revision;
materials.Add(new());
materials[0] = new();
materials.RemoveAt(0);
materials.Clear();
Check(VersionedCollection<MaterialData>.Revision == revision + 4, "Collection mutations not tracked");
materials.Add(new());
for (int i = 0; i < 100; i++) foreach (var material in materials) { }
long allocated = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 1000; i++) foreach (var material in materials) { }
Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Collection enumeration allocated");

string directory = Path.Combine(Path.GetTempPath(), "sponza-checks-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    foreach (int indexType in new[] { 5121, 5123, 5125 })
        foreach (bool binary in new[] { false, true })
            foreach (bool floatUvs in new[] { false, true })
            {
                using var data = new MemoryStream();
                using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
                writer.Write(0); // Buffer-view offset.
                foreach (var position in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY })
                {
                    writer.Write(0); // Accessor offset, with a 16-byte stride.
                    writer.Write(position.X); writer.Write(position.Y); writer.Write(position.Z);
                }
                int uvOffset = (int)data.Position;
                foreach (ushort uv in new ushort[] { 0, 65535, 0 })
                {
                    if (floatUvs)
                    {
                        writer.Write(uv / 65535f); writer.Write(uv / 65535f);
                    }
                    else
                    {
                        writer.Write(uv); writer.Write(uv); writer.Write(0); // Interleaved normalized UVs.
                    }
                }
                int indexOffset = (int)data.Position;
                for (uint i = 0; i < 3; i++)
                {
                    if (indexType == 5121) writer.Write((byte)i);
                    else if (indexType == 5123) writer.Write((ushort)i);
                    else writer.Write(i);
                }
                byte[] bytes = data.ToArray();
                var buffer = new Dictionary<string, object> { ["byteLength"] = bytes.Length };
                if (!binary) buffer["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes);
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    asset = new { version = "2.0" },
                    buffers = new[] { buffer },
                    bufferViews = new[] {
                new { buffer = 0, byteOffset = 4, byteLength = 48, byteStride = 16 },
                new { buffer = 0, byteOffset = uvOffset, byteLength = 24, byteStride = 8 },
                new { buffer = 0, byteOffset = indexOffset, byteLength = bytes.Length - indexOffset, byteStride = 0 } },
                    accessors = new[] {
                new { bufferView = 0, byteOffset = 4, count = 3, componentType = 5126, type = "VEC3" },
                new { bufferView = 1, byteOffset = 0, count = 3, componentType = floatUvs ? 5126 : 5123, type = "VEC2" },
                new { bufferView = 2, byteOffset = 0, count = 3, componentType = indexType, type = "SCALAR" } },
                    meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, TEXCOORD_0 = 1 }, indices = 2 } } } },
                    nodes = new[] { new { mesh = 0, translation = new[] { 0, 0, 0 } }, new { mesh = 0, translation = new[] { 3, 0, 0 } } },
                    scenes = new[] { new { nodes = new[] { 0, 1 } } },
                    scene = 0
                });
                string path = Path.Combine(directory, binary ? "fixture.glb" : "fixture.gltf");
                if (binary)
                {
                    byte[] paddedJson = json.Concat(Enumerable.Repeat((byte)' ', (4 - json.Length % 4) % 4)).ToArray();
                    byte[] paddedData = bytes.Concat(new byte[(4 - bytes.Length % 4) % 4]).ToArray();
                    using var glb = new BinaryWriter(File.Create(path));
                    glb.Write(0x46546C67u); glb.Write(2u); glb.Write((uint)(28 + paddedJson.Length + paddedData.Length));
                    glb.Write(paddedJson.Length); glb.Write(0x4E4F534Au); glb.Write(paddedJson);
                    glb.Write(paddedData.Length); glb.Write(0x004E4942u); glb.Write(paddedData);
                }
                else File.WriteAllBytes(path, json);
                var result = GltfReader.ParseGltfFile(path, Matrix4x4.CreateScale(2), null);
                Check(result.Count == 2 && ReferenceEquals(result[0].Mesh, result[1].Mesh), "Repeated geometry not shared");
                Check(!ReferenceEquals(result[0].Materials, result[1].Materials), "Instance material slots shared");
                Check(result[1].WorldMatrix.Translation == new Vector3(6, 0, 0), "Instance/root transform changed");
                Check(result[0].Mesh.Indices.SequenceEqual(new uint[] { 0, 1, 2 }), "Index decoding failed");
                Check(result[0].Mesh.Vertices[1].Position == Vector3.UnitX, "Strided position decoding failed");
                Check(result[0].Mesh.Vertices[1].UV == new Vector2(1, 0), "Normalized UV decoding/flip failed");
                Check(result[0].Mesh.Vertices[0].Normal == Vector3.UnitZ, "Generated normals changed");
            }
}
finally { Directory.Delete(directory, recursive: true); }
Console.WriteLine("Passed: culling (2,000 randomized cases), off-camera casters, bounds/normal caches, collection invalidation/allocation, and 12 glTF/GLB fixtures.");
