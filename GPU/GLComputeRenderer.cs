using System.Numerics;
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
    private GL gl;
    private uint computeProgram;
    private uint outputTexture;
    private uint rootBuffer;
    private uint width, height;

    public void Initialise(uint width, uint height)
    {
        this.width = width;
        this.height = height;
        WindowOptions opts = WindowOptions.Default;
        // opts.IsVisible = false;
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
#[compute]
#version 430 core
layout(local_size_x = 16, local_size_y = 16) in;

layout(rgba32f, binding = 0) uniform image2D outputImage;

struct Root {
    vec2 position;
};

layout(std430, binding = 1) buffer RootBuffer {
    Root roots[];
};

uniform int rootCount;
uniform vec4 currentColour;
uniform vec2 resolution;
uniform float scale;

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    
    imageStore(outputImage, pixel, vec4(1.));
    if (pixel.x >= int(resolution.x) || pixel.y >= int(resolution.y))
        return;

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
    float intensity = 0.01 / (minDist + 0.01);
    intensity = clamp(intensity, 0.0, 1.0);
    
    // Read existing colour and accumulate
    vec4 newColour = vec4(currentColour.rgb * intensity, 1.0);
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
        Console.WriteLine($"=== Active Uniforms ({uniformCount}) ===");

        for (uint i = 0; i < uniformCount; i++)
        {
            gl.GetActiveUniform(
                computeProgram,
                i,
                256,
                out uint length,
                out int size,
                out GLEnum type,
                out string name
            );

            int location = gl.GetUniformLocation(computeProgram, name);
            Console.WriteLine($"Uniform {i}: {name} (type: {type}, size: {size}, location: {location})");
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

        Console.WriteLine("✓ Compute shader initialized");
    }

    public unsafe void Render(List<Complex> roots, Vector4 colour, double scale)
    {
        if (gl == null)
            throw new InvalidOperationException("Not initialized");

        Console.WriteLine($"Dispatching compute shader with {roots.Count} roots...");

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

        // Set uniforms
        int rootCountLoc = gl.GetUniformLocation(computeProgram, "rootCount");
        gl.Uniform1(rootCountLoc, roots.Count);

        int colourLoc = gl.GetUniformLocation(computeProgram, "currentColour");
        gl.Uniform4(colourLoc, colour.X, colour.Y, colour.Z, colour.W);

        int resolutionLoc = gl.GetUniformLocation(computeProgram, "resolution");
        gl.Uniform2(resolutionLoc, width, height);

        int scaleLoc = gl.GetUniformLocation(computeProgram, "scale");
        gl.Uniform1(scaleLoc, (float)scale);
        Console.WriteLine($"Uniform locations: rootCount={rootCountLoc}, colour={colourLoc}, resolution={resolutionLoc}, scale={scaleLoc}");

        if (resolutionLoc < 0)
        {
            Console.WriteLine("ERROR: resolution uniform not found!");
        }

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

        Console.WriteLine($"Compute shader completed in {sw.ElapsedMilliseconds}ms");
    }

    public Image<Rgba32> GetResult()
    {
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
