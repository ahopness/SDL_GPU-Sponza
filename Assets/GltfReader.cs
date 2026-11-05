using System.Numerics;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SponzaTest.Graphics;

namespace SponzaTest.Assets;

public static class GltfReader
{
    private const int FloatComponent = 5126;
    private const int UnsignedIntComponent = 5125;
    private const int UnsignedShortComponent = 5123;
    private const int UnsignedByteComponent = 5121;

    private readonly record struct BufferView(int Buffer, int ByteOffset, int ByteLength, int ByteStride);
    private readonly record struct Accessor(int BufferView, int ByteOffset, int Count, int ComponentType);
    private readonly record struct AccessorData(byte[] Buffer, int Offset, int Stride, int Count, int ComponentType);

    public static byte[] DecodeImageRgba8(Stream stream, out int width, out int height)
    {
        using var image = Image.Load<Rgba32>(stream);
        width = image.Width;
        height = image.Height;
        byte[] data = new byte[width * height * 4];
        int dst = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Rgba32 p = image[x, y];
                data[dst + 0] = p.R;
                data[dst + 1] = p.G;
                data[dst + 2] = p.B;
                data[dst + 3] = p.A;
                dst += 4;
            }
        }

        return data;
    }

    public static List<(CpuMesh Mesh, List<MaterialData> Materials, Matrix4x4 WorldMatrix)> ParseGltfFile(
        string fullPath,
        Matrix4x4 rootTransform,
        MaterialData? overrideMaterial)
    {
        string baseDir = Path.GetDirectoryName(fullPath) ?? "";
        var (jsonBytes, glbBinChunk) = ReadContainer(fullPath);
        using var doc = JsonDocument.Parse(jsonBytes);
        JsonElement root = doc.RootElement;
        var buffers = ReadBuffers(root, baseDir, glbBinChunk);

        // Load bufferViews
        var bufferViews = new List<BufferView>();
        if (root.TryGetProperty("bufferViews", out var bufferViewElements))
        {
            foreach (var bufferView in bufferViewElements.EnumerateArray())
            {
                int bufferIndex = bufferView.GetProperty("buffer").GetInt32();
                int byteOffset = bufferView.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                int byteLength = bufferView.GetProperty("byteLength").GetInt32();
                int stride = bufferView.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0;
                bufferViews.Add(new BufferView(bufferIndex, byteOffset, byteLength, stride));
            }
        }

        // Load accessors
        var accessors = new List<Accessor>();
        if (root.TryGetProperty("accessors", out var accessorElements))
        {
            foreach (var accessorJson in accessorElements.EnumerateArray())
                accessors.Add(new Accessor(
                    accessorJson.TryGetProperty("bufferView", out var view) ? view.GetInt32() : -1,
                    accessorJson.TryGetProperty("byteOffset", out var offset) ? offset.GetInt32() : 0,
                    accessorJson.GetProperty("count").GetInt32(),
                    accessorJson.GetProperty("componentType").GetInt32()));
        }

        // Load images
        var images = new List<JsonElement>();
        if (root.TryGetProperty("images", out var imgElem))
        {
            foreach (var img in imgElem.EnumerateArray())
                images.Add(img);
        }

        // Load textures
        var textures = new List<int>();
        if (root.TryGetProperty("textures", out var texElem))
        {
            foreach (var tex in texElem.EnumerateArray())
            {
                int src = tex.TryGetProperty("source", out var s) ? s.GetInt32() : -1;
                textures.Add(src);
            }
        }
        var decodedImages = new Dictionary<int, CpuTexture>();

        CpuTexture? GetDecodedImage(int textureIndex)
        {
            if (textureIndex < 0 || textureIndex >= textures.Count)
            {
                return null;
            }
            int imageIndex = textures[textureIndex];
            if (imageIndex < 0 || imageIndex >= images.Count)
            {
                return null;
            }
            if (decodedImages.TryGetValue(imageIndex, out var cached))
            {
                return cached;
            }
            var imgJson = images[imageIndex];
            byte[]? rawBytes = null;
            string name = $"Image_{imageIndex}";
            if (imgJson.TryGetProperty("uri", out var uriProp))
            {
                string uri = uriProp.GetString() ?? "";
                name = uri;
                if (uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    int comma = uri.IndexOf(',');
                    rawBytes = Convert.FromBase64String(uri.Substring(comma + 1));
                }
                else
                {
                    string imgPath = Path.Combine(baseDir, Uri.UnescapeDataString(uri));
                    if (File.Exists(imgPath))
                        rawBytes = File.ReadAllBytes(imgPath);
                }
            }
            else if (imgJson.TryGetProperty("bufferView", out var bufferViewProperty))
            {
                int bufferViewIndex = bufferViewProperty.GetInt32();
                var bufferView = bufferViews[bufferViewIndex];
                rawBytes = new byte[bufferView.ByteLength];
                Buffer.BlockCopy(buffers[bufferView.Buffer], bufferView.ByteOffset, rawBytes, 0, bufferView.ByteLength);
            }
            if (rawBytes == null)
            {
                return null;
            }
            using var ms = new MemoryStream(rawBytes);
            byte[] rgba = DecodeImageRgba8(ms, out int w, out int h);
            var texData = new CpuTexture
            {
                Name = name,
                Width = w,
                Height = h,
                Rgba8Data = rgba
            };
            decodedImages[imageIndex] = texData;

            return texData;
        }

        // Load materials
        var materialsByIndex = new Dictionary<int, MaterialData>();
        if (overrideMaterial == null && root.TryGetProperty("materials", out var matsElem))
        {
            int mIdx = 0;
            foreach (var materialJson in matsElem.EnumerateArray())
            {
                var material = new MaterialData
                {
                    Name = materialJson.TryGetProperty("name", out var n) ? (n.GetString() ?? $"Mat_{mIdx}") : $"Mat_{mIdx}",
                    DoubleSided = materialJson.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean()
                };
                if (materialJson.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var bcf))
                    {
                        var arr = bcf.EnumerateArray();
                        arr.MoveNext();
                        float r = arr.Current.GetSingle();
                        arr.MoveNext();
                        float g = arr.Current.GetSingle();
                        arr.MoveNext();
                        float b = arr.Current.GetSingle();
                        material.BaseColorLinear = new Vector3(r, g, b);
                    }
                    if (pbr.TryGetProperty("baseColorTexture", out var bct) && bct.TryGetProperty("index", out var ti))
                    {
                        material.DiffuseMap = GetDecodedImage(ti.GetInt32());
                    }
                }
                materialsByIndex[mIdx] = material;
                mIdx++;
            }
        }

        AccessorData GetAccessorData(int accessorIndex, int elementSize)
        {
            var accessor = accessors[accessorIndex];
            if (accessor.BufferView < 0)
                throw new NotSupportedException("Accessors without a buffer view are not supported.");
            var view = bufferViews[accessor.BufferView];
            return new AccessorData(
                buffers[view.Buffer], view.ByteOffset + accessor.ByteOffset,
                view.ByteStride > 0 ? view.ByteStride : elementSize,
                accessor.Count, accessor.ComponentType);
        }

        Vector3[] ReadVec3Accessor(int accessorIndex)
        {
            var data = GetAccessorData(accessorIndex, 12);
            var result = new Vector3[data.Count];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = data.Offset + i * data.Stride;
                result[i] = new Vector3(
                    BitConverter.ToSingle(data.Buffer, offset),
                    BitConverter.ToSingle(data.Buffer, offset + 4),
                    BitConverter.ToSingle(data.Buffer, offset + 8));
            }
            return result;
        }

        Vector2[] ReadVec2Accessor(int accessorIndex)
        {
            int componentType = accessors[accessorIndex].ComponentType;
            var data = GetAccessorData(accessorIndex, componentType == FloatComponent ? 8 : 4);
            var result = new Vector2[data.Count];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = data.Offset + i * data.Stride;
                result[i] = componentType switch
                {
                    FloatComponent => new Vector2(
                        BitConverter.ToSingle(data.Buffer, offset),
                        BitConverter.ToSingle(data.Buffer, offset + 4)),
                    UnsignedShortComponent => new Vector2(
                        BitConverter.ToUInt16(data.Buffer, offset) / 65535f,
                        BitConverter.ToUInt16(data.Buffer, offset + 2) / 65535f),
                    _ => Vector2.Zero
                };
            }
            return result;
        }

        uint[] ReadIndicesAccessor(int accessorIndex)
        {
            int componentType = accessors[accessorIndex].ComponentType;
            int elementSize = componentType switch
            {
                UnsignedIntComponent => 4,
                UnsignedShortComponent => 2,
                _ => 1
            };
            var data = GetAccessorData(accessorIndex, elementSize);
            var result = new uint[data.Count];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = data.Offset + i * data.Stride;
                result[i] = componentType switch
                {
                    UnsignedIntComponent => BitConverter.ToUInt32(data.Buffer, offset),
                    UnsignedShortComponent => BitConverter.ToUInt16(data.Buffer, offset),
                    UnsignedByteComponent => data.Buffer[offset],
                    _ => 0
                };
            }
            return result;
        }

        var meshesJson = new List<JsonElement>();
        if (root.TryGetProperty("meshes", out var mElem))
        {
            foreach (var m in mElem.EnumerateArray())
                meshesJson.Add(m);
        }
        var nodesJson = new List<JsonElement>();
        if (root.TryGetProperty("nodes", out var nElem))
        {
            foreach (var n in nElem.EnumerateArray())
                nodesJson.Add(n);
        }
        var results = new List<(CpuMesh Mesh, List<MaterialData> Materials, Matrix4x4 WorldMatrix)>();

        var decodedMeshes = new Dictionary<int, (CpuMesh Mesh, List<MaterialData> Materials)>();

        void ReadMesh(int meshIndex, Matrix4x4 world)
        {
            if (decodedMeshes.TryGetValue(meshIndex, out var cached))
            {
                // Each node owns its material slots, while geometry is shared.
                results.Add((cached.Mesh, new List<MaterialData>(cached.Materials), world * rootTransform));
                return;
            }
            var meshJson = meshesJson[meshIndex];
            var vertices = new List<MeshVertex>();
            var indices = new List<uint>();
            var groups = new List<SubMeshGroup>();
            var materials = new List<MaterialData>();
            foreach (var primitive in meshJson.GetProperty("primitives").EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out var mp) ? mp.GetInt32() : 4;
                if (mode != 4)
                {
                    continue;
                }
                // TRIANGLES only
                var attributes = primitive.GetProperty("attributes");
                if (!attributes.TryGetProperty("POSITION", out var posProp))
                {
                    continue;
                }
                var positions = ReadVec3Accessor(posProp.GetInt32());
                Vector3[]? normals = attributes.TryGetProperty("NORMAL", out var norProp)
                    ? ReadVec3Accessor(norProp.GetInt32())
                    : null;
                Vector2[]? uvs = attributes.TryGetProperty("TEXCOORD_0", out var uvProp)
                    ? ReadVec2Accessor(uvProp.GetInt32())
                    : null;
                uint baseVertex = (uint)vertices.Count;
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 p = positions[i];
                    Vector3 n = normals != null && i < normals.Length ? normals[i] : Vector3.UnitY;
                    Vector2 uv = uvs != null && i < uvs.Length ? new Vector2(uvs[i].X, 1.0f - uvs[i].Y) : Vector2.Zero;
                    vertices.Add(new MeshVertex(p, n, uv));
                }
                int startIndex = indices.Count;
                if (primitive.TryGetProperty("indices", out var idxProp))
                {
                    uint[] primIndices = ReadIndicesAccessor(idxProp.GetInt32());
                    for (int i = 0; i < primIndices.Length; i++)
                        indices.Add(baseVertex + primIndices[i]);
                }
                else
                {
                    for (uint i = 0; i < (uint)positions.Length; i++)
                        indices.Add(baseVertex + i);
                }
                if (normals == null)
                {
                    GenerateNormals(vertices, indices, startIndex);
                }
                MaterialData material;
                if (overrideMaterial != null)
                    material = overrideMaterial;
                else if (primitive.TryGetProperty("material", out var matIdxProp) && materialsByIndex.TryGetValue(matIdxProp.GetInt32(), out var foundMat))
                    material = foundMat;
                else
                    material = new MaterialData();
                int materialSlot = materials.IndexOf(material);
                if (materialSlot < 0)
                {
                    materialSlot = materials.Count;
                    materials.Add(material);
                }
                groups.Add(new SubMeshGroup
                {
                    StartIndex = startIndex,
                    IndexCount = indices.Count - startIndex,
                    MaterialIndex = materialSlot
                });
            }
            if (vertices.Count > 0 && indices.Count > 0)
            {
                var cpuMesh = new CpuMesh
                {
                    Name = meshJson.TryGetProperty("name", out var mn) ? (mn.GetString() ?? "GltfMesh") : "GltfMesh",
                    Vertices = vertices.ToArray(),
                    Indices = indices.ToArray()
                };
                cpuMesh.Groups.AddRange(groups);
                decodedMeshes[meshIndex] = (cpuMesh, materials);
                results.Add((cpuMesh, materials, world * rootTransform));
            }
        }

        void TraverseNode(int nodeIndex, Matrix4x4 parentWorld)
        {
            if (nodeIndex < 0 || nodeIndex >= nodesJson.Count)
            {
                return;
            }
            var node = nodesJson[nodeIndex];
            Matrix4x4 local = ReadLocalTransform(node);
            Matrix4x4 world = local * parentWorld;
            if (node.TryGetProperty("mesh", out var meshProp))
            {
                ReadMesh(meshProp.GetInt32(), world);
            }
            if (node.TryGetProperty("children", out var childrenProp))
            {
                foreach (var child in childrenProp.EnumerateArray())
                    TraverseNode(child.GetInt32(), world);
            }
        }
        if (root.TryGetProperty("scenes", out var scenesElem))
        {
            int sceneIdx = root.TryGetProperty("scene", out var sp) ? sp.GetInt32() : 0;
            int curr = 0;
            foreach (var s in scenesElem.EnumerateArray())
            {
                if (curr == sceneIdx && s.TryGetProperty("nodes", out var rootNodes))
                {
                    foreach (var rn in rootNodes.EnumerateArray())
                        TraverseNode(rn.GetInt32(), Matrix4x4.Identity);
                }
                curr++;
            }
        }

        return results;
    }

    private static (byte[] Json, byte[]? Binary) ReadContainer(string fullPath)
    {
        byte[] jsonBytes;
        byte[]? glbBinChunk = null;
        if (fullPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(fullPath);
            using var br = new BinaryReader(fs);
            uint magic = br.ReadUInt32();
            uint version = br.ReadUInt32();
            uint totalLen = br.ReadUInt32();
            if (magic != 0x46546C67) // 'glTF'
                throw new InvalidDataException($"Not a valid GLB file: {fullPath}");
            uint jsonLen = br.ReadUInt32();
            uint jsonType = br.ReadUInt32();
            jsonBytes = br.ReadBytes((int)jsonLen);
            if (fs.Position + 8 <= totalLen)
            {
                uint binLen = br.ReadUInt32();
                uint binType = br.ReadUInt32();
                glbBinChunk = br.ReadBytes((int)binLen);
            }
        }
        else
        {
            jsonBytes = File.ReadAllBytes(fullPath);
        }
        return (jsonBytes, glbBinChunk);
    }

    private static List<byte[]> ReadBuffers(JsonElement root, string baseDir, byte[]? glbBinChunk)
    {
        var buffers = new List<byte[]>();
        if (root.TryGetProperty("buffers", out var buffersElem))
        {
            int idx = 0;
            foreach (var bufElem in buffersElem.EnumerateArray())
            {
                if (idx == 0 && glbBinChunk != null && !bufElem.TryGetProperty("uri", out _))
                {
                    buffers.Add(glbBinChunk);
                }
                else if (bufElem.TryGetProperty("uri", out var uriProp))
                {
                    string uri = uriProp.GetString() ?? "";
                    if (uri.StartsWith("data:", StringComparison.Ordinal))
                    {
                        int comma = uri.IndexOf(',');
                        buffers.Add(Convert.FromBase64String(uri.Substring(comma + 1)));
                    }
                    else
                    {
                        buffers.Add(File.ReadAllBytes(Path.Combine(baseDir, Uri.UnescapeDataString(uri))));
                    }
                }
                else
                {
                    buffers.Add(glbBinChunk ?? Array.Empty<byte>());
                }
                idx++;
            }
        }

        return buffers;
    }

    private static Matrix4x4 ReadLocalTransform(JsonElement node)
    {
        Matrix4x4 local = Matrix4x4.Identity;
        if (node.TryGetProperty("matrix", out var matProp))
        {
            float[] m = new float[16];
            int k = 0;
            foreach (var v in matProp.EnumerateArray()) m[k++] = v.GetSingle();
            local = new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
        }
        else
        {
            Vector3 t = Vector3.Zero;
            Quaternion r = Quaternion.Identity;
            Vector3 s = Vector3.One;
            if (node.TryGetProperty("translation", out var tp))
            {
                var e = tp.EnumerateArray();
                e.MoveNext();
                float x = e.Current.GetSingle();
                e.MoveNext();
                float y = e.Current.GetSingle();
                e.MoveNext();
                float z = e.Current.GetSingle();
                t = new Vector3(x, y, z);
            }
            if (node.TryGetProperty("rotation", out var rp))
            {
                var e = rp.EnumerateArray();
                e.MoveNext();
                float x = e.Current.GetSingle();
                e.MoveNext();
                float y = e.Current.GetSingle();
                e.MoveNext();
                float z = e.Current.GetSingle();
                e.MoveNext();
                float w = e.Current.GetSingle();
                r = new Quaternion(x, y, z, w);
            }
            if (node.TryGetProperty("scale", out var sp))
            {
                var e = sp.EnumerateArray();
                e.MoveNext();
                float x = e.Current.GetSingle();
                e.MoveNext();
                float y = e.Current.GetSingle();
                e.MoveNext();
                float z = e.Current.GetSingle();
                s = new Vector3(x, y, z);
            }
            local = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
        }

        return local;
    }

    private static void GenerateNormals(List<MeshVertex> vertices, List<uint> indices, int startIndex)
    {
        for (int i = startIndex; i + 2 < indices.Count; i += 3)
        {
            int i0 = (int)indices[i];
            int i1 = (int)indices[i + 1];
            int i2 = (int)indices[i + 2];
            Vector3 n = Vector3.Cross(vertices[i1].Position - vertices[i0].Position, vertices[i2].Position - vertices[i0].Position);
            n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
            var v0 = vertices[i0];
            v0.Normal = n;
            vertices[i0] = v0;
            var v1 = vertices[i1];
            v1.Normal = n;
            vertices[i1] = v1;
            var v2 = vertices[i2];
            v2.Normal = n;
            vertices[i2] = v2;
        }
    }
}
