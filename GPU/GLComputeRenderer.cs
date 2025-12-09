using System.Numerics;

using Microsoft.Extensions.Logging;

using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Polynomials.GPU;

public class GLComputeRenderer : IDisposable
{
    private IWindow? window;
    private GL? gl;
    private uint computeProgram;
    private uint outputTexture;
    private uint rootBuffer;
    private uint parameterBuffer;
    private uint width, height;
    public ILogger? Logger;

    public void Initialise(uint width, uint height)
    {
        this.width = width;
        this.height = height;
        WindowOptions opts = WindowOptions.Default;
        opts.IsVisible = false;
        opts.Size = new Vector2D<int>((int)width, (int)height);
        opts.ShouldSwapAutomatically = false;
        opts.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.Default,
            new APIVersion(4, 3)
        );

        window = Window.Create(opts);
        window.Initialize();
        gl = GL.GetApi(window);

        const string computeShaderSource =
            @"
#version 430 core
layout(local_size_x = 16, local_size_y = 16) in;

layout(rgba32f, binding = 0) uniform image2D outputImage;

struct Root {
    vec2 position;
};

layout(std430, binding = 1) buffer RootBuffer {
    Root roots[];
};

layout(std140, binding = 2) uniform Parameters {
    vec2 resolution;
    vec4 currentColour;
    float scale;
    int rootCount;
};

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    
    if (pixel.x >= int(resolution.x) || pixel.y >= int(resolution.y))
        return;

    // Convert pixel to UV coordinates
    vec2 uv = vec2(pixel) / resolution.y;
    uv = (uv - vec2(0.5 * resolution.x / resolution.y, 0.5)) * 2.0;
    uv *= scale;
    
    // Find minimum distance to any root
    float minDist = 999999.0;
    for (int i = 0; i < rootCount; i++) {
        float dist = distance(uv, roots[i].position);
        minDist = min(minDist, dist);
    }
    
    // Calculate intensity
    float intensity = 1. / (50.*minDist);
    
    // Read existing colour and accumulate
    vec4 newColour = vec4(currentColour.rgb * intensity / 14, 1.0);
    imageStore(outputImage, pixel, newColour);
}
";

        uint computeShader = gl.CreateShader(ShaderType.ComputeShader);
        gl.ShaderSource(computeShader, computeShaderSource);
        gl.CompileShader(computeShader);

        gl.GetShader(computeShader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string log = gl.GetShaderInfoLog(computeShader);
            throw new Exception($"Compute shader compilation failed: {log}");
        }

        computeProgram = gl.CreateProgram();
        gl.AttachShader(computeProgram, computeShader);
        gl.LinkProgram(computeProgram);

        gl.GetProgram(computeProgram, ProgramPropertyARB.LinkStatus, out int linkSuccess);
        if (linkSuccess == 0)
        {
            string log = gl.GetProgramInfoLog(computeProgram);
            throw new Exception($"Compute program linking failed: {log}");
        }
        gl.GetProgram(computeProgram, ProgramPropertyARB.ActiveUniforms, out int uniformCount);
        Logger?.LogDebug("=== Active Uniforms ({}) ===", uniformCount);

        for (uint i = 0; i < uniformCount; i++)
        {
            gl.GetActiveUniform(
                computeProgram,
                i,
                256,
                out _,
                out int size,
                out GLEnum type,
                out string name
            );

            int location = gl.GetUniformLocation(computeProgram, name);
            Logger?.LogDebug("Uniform {}: {} (type: {}, size: {}, location: {})", i, name, type, size, location);
        }

        gl.DeleteShader(computeShader);

        // Create output texture
        outputTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, outputTexture);

        unsafe
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba32f,
                width,
                height,
                0,
                PixelFormat.Rgba,
                PixelType.Float,
                null
            );
        }

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)GLEnum.Nearest
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)GLEnum.Nearest
        );

        parameterBuffer = gl.GenBuffer();

        Logger?.LogInformation("✓ Compute shader initialized");
    }

    public unsafe void Render(List<Complex> roots, Vector4 colour, double scale)
    {
        if (gl == null)
            throw new InvalidOperationException("Not initialized");

        Logger?.LogInformation("Dispatching compute shader with {} roots...", roots.Count);

        // Create SSBO for roots
        if (rootBuffer != 0)
            gl.DeleteBuffer(rootBuffer);

        rootBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, rootBuffer);

        float[] rootData = new float[roots.Count * 2];
        for (int i = 0; i < roots.Count; i++)
        {
            rootData[i * 2] = (float)roots[i].Real;
            rootData[(i * 2) + 1] = (float)roots[i].Imaginary;
        }

        fixed (float* ptr = rootData)
        {
            gl.BufferData(
                BufferTargetARB.ShaderStorageBuffer,
                (nuint)(rootData.Length * sizeof(float)),
                ptr,
                BufferUsageARB.StaticDraw
            );
        }

        // Use compute program
        gl.UseProgram(computeProgram);

        // Bind output image
        gl.BindImageTexture(
            0,
            outputTexture,
            0,
            false,
            0,
            BufferAccessARB.ReadWrite,
            InternalFormat.Rgba32f
        );

        // Bind root buffer
        gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, rootBuffer);

        float[] paramData = new float[10];
        paramData[0] = width;
        paramData[1] = height;
        paramData[4] = colour.X;
        paramData[5] = colour.Y;
        paramData[6] = colour.Z;
        paramData[7] = colour.W;
        paramData[8] = (float)scale;
        paramData[9] = BitConverter.Int32BitsToSingle(roots.Count);

        gl.BindBuffer(BufferTargetARB.UniformBuffer, parameterBuffer);
        fixed (float* ptr = paramData)
        {
            gl.BufferData(
                    BufferTargetARB.UniformBuffer,
                    (nuint)(paramData.Length * sizeof(float)),
                    ptr,
                    BufferUsageARB.DynamicDraw
                );
        }

        gl.BindBufferBase(BufferTargetARB.UniformBuffer, 2, parameterBuffer);
        gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);


        // Dispatch compute shader
        // Work groups of 16x16, so divide by 16 and round up
        uint groupsX = (width + 15) / 16;
        uint groupsY = (height + 15) / 16;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        gl.DispatchCompute(groupsX, groupsY, 1);

        // Wait for completion
        gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);
        gl.Finish(); // Ensure GPU completes
        sw.Stop();

        Logger?.LogInformation("Compute shader completed in {}ms", sw.ElapsedMilliseconds);
    }

    public Image<Rgba32> GetResult()
    {
        if (gl is null)
        {
            throw new InvalidOperationException("Renderer wasn't initialised");
        }

        byte[] pixelData = new byte[width * height * 4 * 4]; // RGBA32F = 16 bytes per pixel

        gl.BindTexture(TextureTarget.Texture2D, outputTexture);

        unsafe
        {
            fixed (byte* ptr = pixelData)
            {
                gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, ptr);
            }
        }

        // Convert float data to bytes
        byte[] rgbaBytes = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* src = pixelData)
            {
                float* floatPtr = (float*)src;
                for (int i = 0; i < width * height * 4; i++)
                {
                    float value = floatPtr[i];
                    rgbaBytes[i] = (byte)Math.Clamp(value * 255, 0, 255);
                }
            }
        }

        var img = Image.LoadPixelData<Rgba32>(rgbaBytes, (int)width, (int)height);
        img.Mutate(x => x.Flip(FlipMode.Vertical));
        return img;
    }

    public void ClearOutput()
    {
        if (gl is null)
        {
            throw new InvalidOperationException("Renderer wasn't initialised");
        }

        gl.BindTexture(TextureTarget.Texture2D, outputTexture);

        float[] clearData = new float[width * height * 4];

        unsafe
        {
            fixed (float* ptr = clearData)
            {
                gl.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    0,
                    0,
                    width,
                    height,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    ptr
                );
            }
        }
    }

    public void Dispose()
    {
        gl?.DeleteTexture(outputTexture);
        gl?.DeleteBuffer(rootBuffer);
        gl?.DeleteProgram(computeProgram);

        GC.SuppressFinalize(this);
    }
}