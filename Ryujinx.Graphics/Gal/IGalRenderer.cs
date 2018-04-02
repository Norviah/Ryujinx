using System;

namespace Ryujinx.Graphics.Gal
{
    public unsafe interface IGalRenderer
    {
        void QueueAction(Action ActionMthd);
        void RunActions();

        void InitializeFrameBuffer();
        void ResetFrameBuffer();
        void Render();
        void SetWindowSize(int Width, int Height);
        void SetFrameBuffer(
            byte* Fb,
            int   Width,
            int   Height,
            float ScaleX,
            float ScaleY,
            float OffsX,
            float OffsY,
            float Rotate);

        //Rasterizer
        void ClearBuffers(int RtIndex, GalClearBufferFlags Flags);

        void SetVertexArray(int VbIndex, int Stride, byte[] Buffer, GalVertexAttrib[] Attribs);

        void RenderVertexArray(int VbIndex);

        //Shader
        void CreateShader(long Tag, GalShaderType Type, byte[] Data);

        void SetShaderConstBuffer(long Tag, int Cbuf, byte[] Data);

        void BindShader(long Tag);

        void BindProgram();

        //Texture
        void UpdateTextures(Func<int, GalShaderType, GalTexture> RequestTextureCallback);
    }
}