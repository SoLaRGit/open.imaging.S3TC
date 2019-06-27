﻿
#region ### copyright, version and changelog information ###
/////////////////////////////////////////////////////////////////////////////// 
// 
// Copyright (c) 2018 Nikola Bozovic. All rights reserved. 
// 
// This code is licensed under the MIT License (MIT). 
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE. 
// 
///////////////////////////////////////////////////////////////////////////////
//
// open.imaging.DXT3
//
// version  : v0.80.18.1113
// author   : Nikola Bozovic <nigerija@gmail.com>
// desc     : LUT optimized software DXT3 (BC2) texture block decompression.
// note     : S3TC patent expired on October 2, 2017.
//            And continuation patent expired on March 16, 2018.
//            S3TC support has landed in Mesa since then.
//
// changelog:
// * 2018.09.20: initial version.
//
// * v0.80.18.1113: optimized DXT3 (BC2) to use LUTs.
//
//    1920x1080 texture decode time on 3GHz CPU:
//      debug   : ~18.1 ms
//        speed in :  ~114.6 mega bytes per sec
//        speed out:  ~458.3 mega bytes per sec
//        speed pix:  ~114.6 mega pixels per sec
//      release : ~16.2 ms
//        speed in :  ~128.0 mega bytes per sec
//        speed out:  ~512.0 mega bytes per sec
//        speed pix:  ~128.0 mega pixels per sec
//
//    Three days ago I had processing times 150~200ms. Now this is scarry 
//    fast even for C# code, have ~91% boost. This code even beats some 
//    c/c++ implementations, and by some. I don't think there is anything
//    else that could be done here to squeeze more time out of this.
//    AVX.Net is still in experimental stage....
//
// PS: if you are extracting and writting images on HDD, you will need 
//     fast SSD, preferably M2 disk to exploit this speed.
//
///////////////////////////////////////////////////////////////////////////////
#endregion

using System;

namespace open.imaging.S3TC
{

  /// <summary>
  /// Defines decompressed output pixel format.
  /// </summary>
  public enum DXT3PixelFormat
  {
    /// <summary>4 byte texel:|B|G|R|A| (also default if incorrect pixel format specified.)</summary>
    BGRA = 0,
    /// <summary>4 byte texel:|R|G|B|A|</summary>
    RGBA = 1,
    /// <summary>4 byte texel:|A|R|G|B|</summary>
    ARGB = 2,
    /// <summary>4 byte texel:|A|B|G|R|</summary>
    ABGR = 3,
  }

  /// <summary>
  /// <para>Software LUT's optimized DXT3 (BC2) texture block decompression.</para>
  /// <para>This is optimized version using LUT's to decode actuall block data, 
  /// with custom output pixel component format.</para>
  /// <para>See also: <seealso cref="SetPixelFormat"/>().</para>
  /// <para>Memory usage:</para>
  /// <para>Total bytes in COLOR(A,R,G,B) static LUT's : 98 320 bytes.</para>
  /// </summary>
  /// <remarks>
  /// <para>NOTICE: LUT's aren't thread safe, meaning if you need different output 
  /// pixel formats you may end with corruped output images.</para>
  /// </remarks>
  unsafe public static class DXT3Decoder
  {

    #region ### comments/docs ###
    //
    // for mathematical formulas go to wiki: 
    // https://en.wikipedia.org/wiki/S3_Texture_Compression    
    // https://www.khronos.org/registry/DataFormat/specs/1.1/dataformat.1.1.html#S3TC
    // Note: this page has bug for BC2, BC3 how to encode colors
    // https://www.khronos.org/opengl/wiki/S3_Texture_Compression
    //
    #endregion

    #region ### internal data ###

    /// <summary>
    /// <para>Default output pixel format.</para>
    /// </summary>
    static DXT3PixelFormat __OutputPixelFormat = DXT3PixelFormat.BGRA;
    /// <summary>
    /// <para>DXT3 (BC2) defines shifting in precalculated alpha.</para>
    /// </summary> 
    static int __DXT3_LUT_COLOR_SHIFT_A = 24;
    /// <summary>
    /// <para>DXT3 (BC2) defines shifting in precalculated [R] component.</para>
    /// </summary>
    static int __DXT3_LUT_COLOR_SHIFT_R = 16;
    /// <summary>
    /// <para>DXT3 (BC2) defines shifting in precalculated [G] component.</para>
    /// </summary>
    static int __DXT3_LUT_COLOR_SHIFT_G = 8;
    /// <summary>
    /// <para>DXT3 (BC2) defines shifting in precalculated [B] component.</para>
    /// </summary>
    static int __DXT3_LUT_COLOR_SHIFT_B = 0;
    /// <summary>
    /// <para>DXT3 (BC2) pre calculated LUT values for alpha codes.</para>
    /// <para>index = (a0 &lt;&lt; 11) | (a1 &lt;&lt; 3) | (a);</para>
    /// </summary> 
    static uint[] __DXT3_LUT_COLOR_VALUE_A;
    /// <summary>
    /// <para>DXT3 (BC2) precalculated LUT values for [R] component for all 4 codes.</para>
    /// <para>index = ((cc0 &lt;&lt; 5) | cc1) &lt;&lt; 2;</para>
    /// </summary>
    static uint[] __DXT3_LUT_COLOR_VALUE_R;
    /// <summary>
    /// <para>DXT3 (BC2) precalculated LUT values for [G] component for all 4 codes.</para>
    /// <para>index = ((cc0 &lt;&lt; 6) | cc1) &lt;&lt; 2;</para>
    /// </summary>
    static uint[] __DXT3_LUT_COLOR_VALUE_G;
    /// <summary>
    /// <para>DXT3 (BC2) precalculated LUT values for [B] component for all 4 codes.</para>
    /// <para>index = ((cc0 &lt;&lt; 5) | cc1) &lt;&lt; 2;</para>
    /// </summary>
    static uint[] __DXT3_LUT_COLOR_VALUE_B;

    #endregion

    #region ### static ctor ###

    /// <summary>
    /// initializes static data
    /// </summary>
    static DXT3Decoder()
    {
      // execution time: less than 1 ms
      __DXT3_LUT_COLOR_VALUE_RGB_Build();      
      
      // execution time: little less than 3 ms
      __DXT3_LUT_COLOR_VALUE_A_Build();
    }

    #endregion

    #region ### public methods ###

    /// <summary>
    /// <para>DXT3 (BC2)</para>
    /// Returns expected input buffers size in bytes for specified width and height.
    /// </summary>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    public static int InputBufferSize(int width, int height)
    {
      // number of blocks by width * number of blocks by height * block size in bytes
      return ((width + 3) / 4) * ((height + 3) / 4) * 16;
    }

    /// <summary>
    /// <para>DXT3 (BC2)</para>
    /// Returns expected output buffers size in bytes for specified width and height.
    /// </summary>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    public static int OutputBufferSize(int width, int height)
    {
      // A,R,G,B are 8bit components, and you really need comments ;)
      return width * height * 4;
    }

/// <summary>
    /// <para>DXT5 (BC3)</para>
    /// <para>Allows override of default output pixel format, which dictates how LUT's are built.</para>
    /// <remarks>NOTICE: LUT's will be rebuilt only if pixelFomat is not same as current.</remarks>
    /// </summary>
    /// <param name="pixelFormat">Defines decoding output pixel format.</param>
    public static void SetPixelFormat(DXT3PixelFormat pixelFormat)
    {
      bool rebuildLut = (__OutputPixelFormat != pixelFormat);
      __OutputPixelFormat = pixelFormat;
      switch (__OutputPixelFormat)
      {
        case DXT3PixelFormat.ABGR:
          __DXT3_LUT_COLOR_SHIFT_A = 0;
          __DXT3_LUT_COLOR_SHIFT_B = 8;
          __DXT3_LUT_COLOR_SHIFT_G = 16;
          __DXT3_LUT_COLOR_SHIFT_R = 24;
          break;
        case DXT3PixelFormat.ARGB:
          __DXT3_LUT_COLOR_SHIFT_A = 0;
          __DXT3_LUT_COLOR_SHIFT_R = 8;
          __DXT3_LUT_COLOR_SHIFT_G = 16;
          __DXT3_LUT_COLOR_SHIFT_B = 24;
          break;
        case DXT3PixelFormat.RGBA:
          __DXT3_LUT_COLOR_SHIFT_R = 0;
          __DXT3_LUT_COLOR_SHIFT_G = 8;
          __DXT3_LUT_COLOR_SHIFT_B = 16;
          __DXT3_LUT_COLOR_SHIFT_A = 24;
          break;
        case DXT3PixelFormat.BGRA:
        default:
          __DXT3_LUT_COLOR_SHIFT_B = 0;
          __DXT3_LUT_COLOR_SHIFT_G = 8;
          __DXT3_LUT_COLOR_SHIFT_R = 16;
          __DXT3_LUT_COLOR_SHIFT_A = 24;
          break;
      }
      if (rebuildLut || null == __DXT3_LUT_COLOR_VALUE_R)
      {
        __DXT3_LUT_COLOR_VALUE_A_Build();
        __DXT3_LUT_COLOR_VALUE_RGB_Build();
      }
    }

    #endregion

    #region ### DXT3 (BC2) LOOK-UP TABLE'S (LUT's) ###

    #pragma warning disable 1587

    /// <summary>
    /// <para>DXT3 (BC2)</para>
    /// <para>Builds static __DXT3_LUT_COLOR_VALUE_A[] look-up table.</para>
    /// </summary>
    static void __DXT3_LUT_COLOR_VALUE_A_Build()
    {
      unchecked
      {
        __DXT3_LUT_COLOR_VALUE_A = new uint[16]; // 16

        // DXT3 (BC2) pre calculated values for a codes
        byte[] __DXT3_LUT_16 = new byte[16] // 0x00 - 0x0f (0-15)
          {
              0,  17,  34,  51,  68,  85, 102, 119, 
            136, 153, 170, 187, 204, 221, 238, 255
          };
  
        for (int i = 0; i <= 15; i++)
        {
          __DXT3_LUT_COLOR_VALUE_A[i] = (uint)__DXT3_LUT_16[i] << __DXT3_LUT_COLOR_SHIFT_A;
        }
       
      } // unchecked
    }

    /// <summary>
    /// <para>DXT3 (BC2)</para>
    /// <para>Builds static __DXT3_LUT_COLOR_VALUE_{R,G,B}[] look-up table(s).</para>
    /// </summary>
    static void __DXT3_LUT_COLOR_VALUE_RGB_Build()
    {
      // DXT3 (BC2) pre calculated values for r & b codes
      byte[] __DXT3_LUT_4x8 = // 0x00 - 0x1f (0-31)
        { 
            0,   8,  16,  25,  33,  41,  49,  58, 
           66,  74,  82,  90,  99, 107, 115, 123, 
          132, 140, 148, 156, 164, 173, 181, 189, 
          197, 205, 214, 222, 230, 238, 247, 255 
        };

      // DXT3 (BC2) pre calculated values for g codes
      byte[] __DXT3_LUT_8x8 = // 0x00 - 0x3f (0-63)
        {
            0,   4,   8,  12,  16,  20,  24,  28, 
           32,  36,  40,  45,  49,  53,  57,  61, 
           65,  69,  73,  77,  81,  85,  89,  93, 
           97, 101, 105, 109, 113, 117, 121, 125, 
          130, 134, 138, 142, 146, 150, 154, 158, 
          162, 166, 170, 174, 178, 182, 186, 190, 
          194, 198, 202, 206, 210, 214, 219, 223, 
          227, 231, 235, 239, 243, 247, 251, 255 
        };

      __DXT3_LUT_COLOR_VALUE_R = new uint[4096];  // 4*32*32
      __DXT3_LUT_COLOR_VALUE_G = new uint[16384]; // 4*64*64
      __DXT3_LUT_COLOR_VALUE_B = new uint[4096];  // 4*32*32

      for (int cc0 = 0; cc0 < 32; cc0++)
      { 
        for (int cc1 = 0; cc1 < 32; cc1++)
        {
          int index = ((cc0 << 5) | cc1) << 2;
          __DXT3_LUT_COLOR_VALUE_R[index | 0] = (uint)(((uint)__DXT3_LUT_4x8[cc0]) << __DXT3_LUT_COLOR_SHIFT_R);
          __DXT3_LUT_COLOR_VALUE_B[index | 0] = (uint)(((uint)__DXT3_LUT_4x8[cc0]) << __DXT3_LUT_COLOR_SHIFT_B);
          __DXT3_LUT_COLOR_VALUE_R[index | 1] = (uint)(((uint)__DXT3_LUT_4x8[cc1]) << __DXT3_LUT_COLOR_SHIFT_R);
          __DXT3_LUT_COLOR_VALUE_B[index | 1] = (uint)(((uint)__DXT3_LUT_4x8[cc1]) << __DXT3_LUT_COLOR_SHIFT_B);
          // Each RGB image data block is encoded according to the BC1 formats, 
          // with the exception that the two code bits always use the non-transparent encodings. 
          // In other words, they are treated as though color0 > color1, 
          // regardless of the actual values of color0 and color1.
          // p2 = ((2*c0)+(c1))/3
          __DXT3_LUT_COLOR_VALUE_R[index | 2] = (uint)((uint)((byte)(((__DXT3_LUT_4x8[cc0] * 2) + (__DXT3_LUT_4x8[cc1])) / 3)) << __DXT3_LUT_COLOR_SHIFT_R);
          __DXT3_LUT_COLOR_VALUE_B[index | 2] = (uint)((uint)((byte)(((__DXT3_LUT_4x8[cc0] * 2) + (__DXT3_LUT_4x8[cc1])) / 3)) << __DXT3_LUT_COLOR_SHIFT_B);
          // p3 = ((c0)+(2*c1))/3
          __DXT3_LUT_COLOR_VALUE_R[index | 3] = (uint)((uint)((byte)(((__DXT3_LUT_4x8[cc0]) + (__DXT3_LUT_4x8[cc1] * 2)) / 3)) << __DXT3_LUT_COLOR_SHIFT_R);
          __DXT3_LUT_COLOR_VALUE_B[index | 3] = (uint)((uint)((byte)(((__DXT3_LUT_4x8[cc0]) + (__DXT3_LUT_4x8[cc1] * 2)) / 3)) << __DXT3_LUT_COLOR_SHIFT_B);
        }//cc1
      }//cc0
      for (int cc0 = 0; cc0 < 64; cc0++)
      { 
        for (int cc1 = 0; cc1 < 64; cc1++)
        {
          int index = ((cc0 << 6) | cc1) << 2;
          __DXT3_LUT_COLOR_VALUE_G[index | 0] = (uint)(((uint)__DXT3_LUT_8x8[cc0]) << __DXT3_LUT_COLOR_SHIFT_G);
          __DXT3_LUT_COLOR_VALUE_G[index | 1] = (uint)(((uint)__DXT3_LUT_8x8[cc1]) << __DXT3_LUT_COLOR_SHIFT_G);
          // Each RGB image data block is encoded according to the BC1 formats, 
          // with the exception that the two code bits always use the non-transparent encodings. 
          // In other words, they are treated as though color0 > color1, 
          // regardless of the actual values of color0 and color1.
          // p2 = ((2*c0)+(c1))/3
          __DXT3_LUT_COLOR_VALUE_G[index | 2] = (uint)((uint)((byte)(((__DXT3_LUT_8x8[cc0] * 2) + (__DXT3_LUT_8x8[cc1])) / 3)) << __DXT3_LUT_COLOR_SHIFT_G);
          // p3 = ((c0)+(2*c1))/3
          __DXT3_LUT_COLOR_VALUE_G[index | 3] = (uint)((uint)((byte)(((__DXT3_LUT_8x8[cc0]) + (__DXT3_LUT_8x8[cc1] * 2)) / 3)) << __DXT3_LUT_COLOR_SHIFT_G);
        }//cc1
      }//cc0
    }

    #pragma warning restore 1587

    #endregion

    #region ### Decode ###

    /// <summary>
    /// <para>DXT3 (BC2) -> {R,G,B,A} 8-bit components with LUTs precalculated pixel format.</para>
    /// Decompresses all <paramref name="p_input">input</paramref> (BC2) blocks of a compressed image 
    /// and stores the result pixel values into <paramref name="p_output">output</paramref>.
    /// </summary>
    /// <param name="width">image width.</param>
    /// <param name="height">image height.</param>
    /// <param name="p_input">pointer to compressed DXT3 (BC2) blocks, we want to decompress.</param>
    /// <param name="p_output">pointer to the image where the decoded pixels will be stored.</param>
    public static void Decode(uint width, uint height, void* p_input, void* p_output)
    {
      unchecked
      {
        byte* source = (byte*)p_input;
        uint* target = (uint*)p_output;
        uint target_4scans = (width << 2);
        uint x_block_count = (width + 3) >> 2;
        uint y_block_count = (height + 3) >> 2;

        //############################################################
        if ((x_block_count << 2) != width || (y_block_count << 2) != height)
        {
          // for images that do not fit in 4x4 texel bounds
          goto ProcessWithCheckingTexelBounds;
        }
        //############################################################
        //ProcessWithoutCheckingTexelBounds:
        //
        // NOTICE: source and target ARE aligned as 4x4 texels
        //
        // target : advance by 4 scan lines
        for (uint y_block = 0; y_block < y_block_count; y_block++, target += target_4scans)
        {
          uint* texel_x = target;
          // texel: advance by 4 texels
          for (uint x_block = 0; x_block < x_block_count; x_block++, source += 16, texel_x += 4)
          {
            // read DXT3 (BC2) block data
            ulong aclut = *(ulong*)(source);        // 00-07 : a LUT    (64bits) 4x4x4bits
            ushort cc0 = *(ushort*)(source + 8);    // 08-09 : cc0      (16bits)
            ushort cc1 = *(ushort*)(source + 10);   // 0a-0b : cc1      (16bits)
            uint ccfnlut = *(uint*)(source + 12);   // 0c-0f : ccfn LUT (32bits) 4x4x2bits

            // alpha code and color code [r,g,b] indexes to luts         
            uint ccr = ((uint)((cc0 & 0xf800) >> 4) | (uint)((cc1 & 0xf800) >> 9));
            uint ccg = ((uint)((cc0 & 0x07E0) << 3) | (uint)((cc1 & 0x07E0) >> 3));
            uint ccb = ((uint)((cc0 & 0x001F) << 7) | (uint)((cc1 & 0x001F) << 2));

            // process 4x4 texels
            uint* texel = texel_x;
            for (uint by = 0; by < 4; by++, texel += width)
            {
              for (uint bx = 0; bx < 4; bx++, aclut >>= 4, ccfnlut >>= 2)
              {              
                uint ac = (uint)(aclut & 0x0f);                
                uint ccfn = (uint)(ccfnlut & 0x03);
                
                *(texel + bx) = (uint)
                  (
                    __DXT3_LUT_COLOR_VALUE_A[ac] |
                    __DXT3_LUT_COLOR_VALUE_R[ccr | ccfn] |
                    __DXT3_LUT_COLOR_VALUE_G[ccg | ccfn] |
                    __DXT3_LUT_COLOR_VALUE_B[ccb | ccfn]
                  );
              }//bx
            }//by
          }//x_block
        }//y_block
        return;
        //
        //############################################################
        // NOTICE: source and target ARE NOT aligned to 4x4 texels, 
        //         We must check for End Of Image (EOI) in this case.
        //############################################################
        // lazy to write boundary separate processings.
        // Just end of image (EOI) pointer check only.
        // considering that I have encountered few images that are not
        // aligned to 4x4 texels, this should be almost never called.
        // takes ~500us (0.5ms) more time processing 2MB pixel images.
        //############################################################
        //
      ProcessWithCheckingTexelBounds:
        uint* EOI = target + (width * height); // ok, one multiply op ;)
        // target : advance by 4 scan lines
        for (uint y_block = 0; y_block < y_block_count; y_block++, target += target_4scans)
        {
          uint* texel_x = target;
          // texel: advance by 4 texels
          for (uint x_block = 0; x_block < x_block_count; x_block++, source += 16, texel_x += 4)
          {
            // read DXT3 (BC2) block data
            ulong aclut = *(ulong*)(source);        // 00-07 : a LUT    (64bits) 4x4x4bits
            ushort cc0 = *(ushort*)(source + 8);    // 08-09 : cc0      (16bits)
            ushort cc1 = *(ushort*)(source + 10);   // 0a-0b : cc1      (16bits)
            uint ccfnlut = *(uint*)(source + 12);   // 0c-0f : ccfn LUT (32bits) 4x4x2bits

            // alpha code and color code [r,g,b] indexes to lut values           
            uint ccr = ((uint)((cc0 & 0xf800) >> 4) | (uint)((cc1 & 0xf800) >> 9));
            uint ccg = ((uint)((cc0 & 0x07E0) << 3) | (uint)((cc1 & 0x07E0) >> 3));
            uint ccb = ((uint)((cc0 & 0x001F) << 7) | (uint)((cc1 & 0x001F) << 2));

            // process 4x4 texels
            uint* texel = texel_x;
            for (uint by = 0; by < 4; by++, texel += width)
            {
              //############################################################
              // Check Y Bound (break: no more texels available for block)
              if (texel >= EOI) break;
              //############################################################
              for (uint bx = 0; bx < 4; bx++, aclut >>= 4, ccfnlut >>= 2)
              {              
                //############################################################
                // Check X Bound (continue: need ac|ccfnlut to complete shift)
                if (texel + bx >= EOI) continue;
                //############################################################
                uint ac = (uint)(aclut & 0x0f);
                uint ccfn = (uint)(ccfnlut & 0x03);
                
                *(texel + bx) = (uint)
                  (
                    __DXT3_LUT_COLOR_VALUE_A[ac] |
                    __DXT3_LUT_COLOR_VALUE_R[ccr | ccfn] |
                    __DXT3_LUT_COLOR_VALUE_G[ccg | ccfn] |
                    __DXT3_LUT_COLOR_VALUE_B[ccb | ccfn]
                  );
              }//bx
            }//by
          }//x_block
        }//y_block
      }//unchecked
    }

    #endregion
  }
}
