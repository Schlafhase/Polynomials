using System.Drawing;
using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Polynomials.GPU;

public class ShaderRenderer : IDisposable
{
    private IWindow? window;
    private GL? gl;
    private uint vao;
    private uint vbo;
    private uint shaderProgram;
    private uint framebuffer;
    private uint renderTexture;
    private uint width;
    private uint height;

    public void Initialise(uint width, uint height, string shaderSource)
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
            new APIVersion(3, 3)
        );

        window = Window.Create(opts);
        window.Initialize();
        initialiseGL(shaderSource);
    }

    private void initialiseGL(string shaderSource)
    {
        gl = GL.GetApi(window);

        // Vertex shader
        string vertexShaderSource =
            @"
                #version 330 core
                layout (location = 0) in vec2 aPosition;
                layout (location = 1) in vec2 aTexCoord;
                
                out vec2 TexCoord;

                void main()
                {
                    gl_Position = vec4(aPosition, 0., 1.0);
                    TexCoord = aTexCoord;
                }
            ";

        // Fragment shader - implements Newton's method to find roots
        string fragmentShaderSource =
            @"
                #version 330 core
                in vec2 TexCoord;
                out vec4 FragColour;

            uniform vec2 uResolution;
            uniform sampler2D inputTexture;
            uniform samplerBuffer roots;
            uniform int rootCount;
            uniform float scale;
            uniform vec4 currentColour;

            "
            + shaderSource
            + @"

            void main() {
                FragColour = fragmentShader();
            }
                ";

        // Compile shaders
        uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertexShader, vertexShaderSource);
        gl.CompileShader(vertexShader);
        checkShaderCompilation(vertexShader, "Vertex");

        uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragmentShader, fragmentShaderSource);
        gl.CompileShader(fragmentShader);
        checkShaderCompilation(fragmentShader, "Fragment");

        // Link shader program
        shaderProgram = gl.CreateProgram();
        gl.AttachShader(shaderProgram, vertexShader);
        gl.AttachShader(shaderProgram, fragmentShader);
        gl.LinkProgram(shaderProgram);
        Console.WriteLine("=== Shader Uniforms ===");
        gl.GetProgram(shaderProgram, ProgramPropertyARB.ActiveUniforms, out int uniformCount);
        for (int i = 0; i < uniformCount; i++)
        {
            gl.GetActiveUniform(
                shaderProgram,
                (uint)i,
                100,
                out _,
                out _,
                out GLEnum _,
                out string name
            );
            int location = gl.GetUniformLocation(shaderProgram, name);
            Console.WriteLine($"Uniform {i}: {name} (location: {location})");
        }
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        float[] quadVertices =
        {
            // Positions    // TexCoords
            -1f,
            -1f,
            0f,
            0f, // Bottom-left
            1f,
            -1f,
            1f,
            0f, // Bottom-right
            1f,
            1f,
            1f,
            1f, // Top-right
            -1f,
            -1f,
            0f,
            0f, // Bottom-left
            1f,
            1f,
            1f,
            1f, // Top-right
            -1f,
            1f,
            0f,
            1f, // Top-left
        }; // Full-screen quad

        vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        unsafe
        {
            fixed (float* v = quadVertices)
            {
                gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(quadVertices.Length * sizeof(float)),
                    v,
                    BufferUsageARB.StaticDraw
                );
            }

            gl.VertexAttribPointer(
                0,
                2,
                VertexAttribPointerType.Float,
                false,
                4 * sizeof(float),
                (void*)0
            );
            gl.EnableVertexAttribArray(0);

            gl.VertexAttribPointer(
                1,
                2,
                VertexAttribPointerType.Float,
                false,
                4 * sizeof(float),
                (void*)(2 * sizeof(float))
            );
            gl.EnableVertexAttribArray(1);
        }

        gl.BindVertexArray(0);

        framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

        renderTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, renderTexture);

        unsafe
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)InternalFormat.Rgba8,
                width,
                height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                null
            );
        }

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)GLEnum.Linear
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)GLEnum.Linear
        );

        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            renderTexture,
            0
        );

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new Exception($"Framebuffer incomplete: {status}");
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private unsafe uint createComplexListBuffer(List<Complex> numbers)
    {
        uint bufferId = gl!.GenBuffer();
        gl.BindBuffer(BufferTargetARB.TextureBuffer, bufferId);

        float[] data = new float[numbers.Count * 2];
        for (int i = 0; i < numbers.Count; i++)
        {
            data[i * 2] = (float)numbers[i].Real;
            data[i * 2 + 1] = (float)numbers[i].Imaginary;
        }

        fixed (float* ptr = data)
        {
            gl.BufferData(
                BufferTargetARB.TextureBuffer,
                (nuint)(data.Length * sizeof(float)),
                ptr,
                BufferUsageARB.StaticDraw
            );
        }

        uint textureId = gl.GenTexture();
        gl.BindTexture(TextureTarget.TextureBuffer, textureId);
        gl.TexBuffer(GLEnum.TextureBuffer, SizedInternalFormat.RG32f, bufferId);

        // DEBUG: Read back to verify
        float[] readBack = new float[data.Length];
        fixed (float* readPtr = readBack)
        {
            gl.GetBufferSubData(
                BufferTargetARB.TextureBuffer,
                0,
                (nuint)(4 * sizeof(float)),
                readPtr
            );
        }

        Console.WriteLine(
            $"Texture created. Uploaded {readBack.Length} floats out of {data.Length}, read back first 4: {readBack[0]}, {readBack[1]}, {readBack[2]}, {readBack[3]}; should be: {data[0]}, {data[1]}, {data[2]}, {data[3]}"
        );

        gl.BindBuffer(BufferTargetARB.TextureBuffer, 0);
        gl.BindTexture(TextureTarget.TextureBuffer, 0);

        return bufferId;
    }

    public byte[] Render(uint inputTexture, Vector4 currentColour, List<Complex> roots)
    {
        if (gl == null)
            throw new InvalidOperationException("Renderer not initialized");

        // Bind framebuffer
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);

        // Clear and set up render state
        gl.ClearColor(System.Drawing.Color.Black);
        gl.Clear(ClearBufferMask.ColorBufferBit);

        // Use shader program
        gl.UseProgram(shaderProgram);

        // Set uniforms
        int resolutionLoc = gl.GetUniformLocation(shaderProgram, "uResolution");
        gl.Uniform2(resolutionLoc, new Vector2(width, height));

        int currentColourLoc = gl.GetUniformLocation(shaderProgram, "currentColour");
        gl.Uniform4(currentColourLoc, ref currentColour);

        int rootCountLoc = gl.GetUniformLocation(shaderProgram, "rootCount");
        gl.Uniform1(rootCountLoc, roots.Count);

        // Bind input texture to texture unit 0
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, inputTexture);
        int textureLoc = gl.GetUniformLocation(shaderProgram, "inputTexture");
        gl.Uniform1(textureLoc, 0);

        uint rootBuffer = createComplexListBuffer(roots);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.TextureBuffer, rootBuffer);

        int rootLoc = gl.GetUniformLocation(shaderProgram, "roots");
        gl.Uniform1(rootLoc, 1);
        gl.ActiveTexture(TextureUnit.Texture0);
        Console.WriteLine(
            $"Uniform locations: uResolution={resolutionLoc}, currentColour={currentColourLoc}"
        );
        Console.WriteLine(
            $"  inputTexture={textureLoc}, complexArray={rootLoc}, complexArray_size={rootCountLoc}"
        );

        // Draw full-screen quad
        gl.BindVertexArray(vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        // Read pixels from framebuffer
        byte[] pixelData = new byte[width * height * 4]; // RGBA
        unsafe
        {
            fixed (byte* ptr = pixelData)
            {
                gl.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        // Clean up
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        return pixelData;
    }

    public byte[] Render(Image<Rgba32> inputImage, Vector4 currentColour, List<Complex> roots)
    {
        // Create texture from image
        uint textureId = CreateTextureFromImage(inputImage);

        // Render using the texture
        byte[] result = Render(textureId, currentColour, roots);

        // Clean up the temporary texture
        gl!.DeleteTexture(textureId);

        return result;
    }

    public Image<Rgba32> RenderToImage(
        Image<Rgba32> inputImage,
        Vector4 currentColour,
        List<Complex> roots
    )
    {
        byte[] pixelData = Render(inputImage, currentColour, roots);

        // Create ImageSharp image from pixel data
        // Note: pixelData is in RGBA format from OpenGL
        return Image.LoadPixelData<Rgba32>(pixelData, (int)width, (int)height);
    }

    public uint CreateTextureFromImage(Image<Rgba32> img)
    {
        uint textureId = gl!.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, textureId);

        byte[] imgData = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(imgData);

        unsafe
        {
            fixed (byte* data = imgData)
            {
                gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)InternalFormat.Rgba8,
                    (uint)img.Width,
                    (uint)img.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    data
                );
            }
        }

        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)GLEnum.Linear
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)GLEnum.Linear
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)GLEnum.ClampToEdge
        );
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)GLEnum.ClampToEdge
        );

        gl.BindTexture(TextureTarget.Texture2D, 0);

        return textureId;
    }

    private void checkShaderCompilation(uint shader, string type)
    {
        gl!.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = gl.GetShaderInfoLog(shader);
            throw new Exception($"{type} shader compilation failed: {infoLog}");
        }
    }

    public void Dispose()
    {
        if (gl != null)
        {
            gl.DeleteVertexArray(vao);
            gl.DeleteBuffer(vbo);
            gl.DeleteProgram(shaderProgram);
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteTexture(renderTexture);
        }

        window?.Dispose();
        GC.SuppressFinalize(this);
    }
}
