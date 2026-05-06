using System;
using System.Runtime.InteropServices;
using DurangoTypes;

namespace ForzaTechStudio;

public static class XGImports
{
    [DllImport("xg.dll", EntryPoint = "XGCreateTexture2DComputer", CallingConvention = CallingConvention.Cdecl)]
    public unsafe static extern int XGCreateTexture2DComputer(XG_TEXTURE2D_DESC* desc, XGTextureAddressComputer** computer);
}
