﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GameRes.Formats.Kid
{
    public class BipFormat : ImageFormat
    {
        public override string Tag { get { return "BIP/PS2"; } }
        public override string Description { get { return "PS2 tiled bitmap format"; } }
        public override uint Signature { get { return 0; } } //0x05000000

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            uint header = file.ReadUInt32();
            uint real_sign;
            bool multi = false; // not implemented
            if (header < 5)
            {
                return null;
            }
            else if (header == 5)
            {
                real_sign = 0x14;
            }
            else if (header >= 6 && header <= 0x0C)
            {
                real_sign = header * 4;
                multi = true;
            }
            else
            {
                return null; // throw new NotSupportedException(string.Format("BIP Chara format not supported."));
            }

            file.Seek(0x10, SeekOrigin.Begin);
            uint fstart = file.ReadUInt16(); // usually 0x100

            // Non-Sequential read
            file.Seek(0x88, SeekOrigin.Begin); // next one +20
            uint width = file.ReadUInt16();
            uint height = file.ReadUInt16();
            if (width > 2560 || height > 1440 || width < 16 || height < 16) // suppose not so large or so small
                return null;

            file.Seek(real_sign, SeekOrigin.Begin); // usually 0x14
            if ((file.ReadUInt16() & 0x7FFF) != fstart)
            {
                return null;
            }
            uint sign = file.ReadUInt16();
            uint dy;
            bool sliced = true;
            long denominator = file.Length;
            if (multi)
            {
                file.Seek(0xA2, SeekOrigin.Begin);
                uint f2start = file.ReadUInt16();
                denominator = f2start * 1024 + fstart + 0x100;
            }
            double oversize = (double)width * height * 4 / denominator;

            file.Seek(0x90, SeekOrigin.Begin);
            //uint size = file.ReadUInt32(); // not size
            uint sizesign = file.ReadUInt16();
            uint sizesign_high = file.ReadUInt16();
            //size &= 0x00FFFFFF;
            //if (sign == 0x13 && size != (file.Length - 0x100) || sign == 0x17 && size != (file.Length - 0x100)* 2)
            if (sizesign != 0 || sizesign_high == 0)
            {
                return null;
            }
            uint pixelsize = (sizesign_high & 0x00FF) * 0x10000; /*r16: 2*pixelsize r32: pixelsize*/

            if (oversize > 0.87 /*sign == 0x13*/)
            {
                // no-sliced oversize min: 0.87178 max: 0.9866
                // sliced oversize max: 0.8333 min: 0.76
                dy = 16;
                sliced = false;
                // sign = focusT / 2
            }
            else if (sign > 0x50)
            {
                return null;
            }
            else if (((double)pixelsize / denominator) <= 1.045 && sign != 0x2E && sign != 0x33)
            {
                /*known oversize 1.02 for expected <=1, noticed smallest r16 oversize 1.045+ for MOAR2 EV_SP04A, 0.99+ for MOAR1 OM2_TT13A, 0.89+ for MOAR3 C3_30A*/
                //sign % 2 == 0 && sign <= 0x2A && sign != 0x22 && sign != 0x1A
                //sign == 0x16 || sign == 0x20 || sign == 0x2A
                dy = 32;
                // sign = focusT * 2
            }
            else
            {
                //sign == 0x17 || sign == 0x19 || sign == 0x1F || sign == 0x25 || sign == 0x2E || sign == 0x34
                dy = 16;
                // sign = focusT / 2
            }

            // uint dx = dy * 32;

            return new BipImageMetaData
            {
                Width = width,
                Height = height,
                BlockSize = dy,
                Sliced = sliced,
                Multi = multi,
            };
        }
        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            if (info == null)
                throw new NotSupportedException(string.Format("Not BIP texture format."));
            var bipheader = (BipImageMetaData)info;
            file.Seek(0x10, SeekOrigin.Begin);
            uint fstart = file.ReadUInt16(); // usually 0x100
            file.Seek(fstart, SeekOrigin.Begin);
            byte[] pixels = new byte[bipheader.iWidth * bipheader.iHeight * 4];
            uint dy = bipheader.BlockSize;
            uint dx = dy * 32;
            if (bipheader.Sliced)
            {
                long dwidth = ((bipheader.iWidth + (dy - 2) - 1) / (dy - 2)) * dy;
                long dheight = ((bipheader.iHeight + (dy - 2) - 1) / (dy - 2)) * dy;
                long focus_H = (dwidth* dheight + dx - 1) / dx;
                long focus_T = (focus_H + dy - 1) / dy;
                
                for (int t = 0; t < focus_T; t++)
                {
                    for(int y = 0; y < dy; y++)
                    {
                        for (int x = 0; x < dx; x++)
                        {
                            var pixel = file.ReadBytes(4); //RGBA with wrong A
                            long i2x = x + t * dx;
                            long i3t = i2x / dwidth;
                            long i3x = i2x - i3t * dwidth;
                            long i3y = i3t * (dy - 2) + y;
                            long i4x = i3x - i3x / dy * dy + i3x / dy * (dy - 2);
                            if (i3x >= dwidth || i4x >= bipheader.iWidth || i3y >= bipheader.iHeight)
                                continue;
                            long target = (i4x + i3y * bipheader.iWidth) * 4;
                            //BGRA
                            pixels[target] = pixel[2];
                            pixels[target + 1] = pixel[1];
                            pixels[target + 2] = pixel[0];
                            if (pixel[3] >= byte.MaxValue / 2)
                                pixels[target + 3] = byte.MaxValue;
                            else
                                pixels[target + 3] = (byte)(pixel[3] << 1);
                        }
                    }
                }
            }
            else
            {
                long focus_H = (bipheader.iWidth * bipheader.iHeight + dx - 1) / dx;
                long focus_T = (focus_H + dy - 1) / dy;
                for (int t = 0; t < focus_T; t++)
                {
                    for (int y = 0; y < dy; y++)
                    {
                        for (int x = 0; x < dx; x++)
                        {
                            var pixel = file.ReadBytes(4); //RGBA with wrong A
                            long i2x = x + t * dx;
                            long i3t = i2x / bipheader.iWidth;
                            long i3x = i2x - i3t * bipheader.iWidth;
                            long i3y = i3t * dy + y;
                            if (i3x >= bipheader.iWidth || i3y >= bipheader.iHeight)
                                continue;
                            long target = (i3x + i3y * bipheader.iWidth) * 4;
                            //BGRA
                            pixels[target] = pixel[2];
                            pixels[target + 1] = pixel[1];
                            pixels[target + 2] = pixel[0];
                            if (pixel[3] >= byte.MaxValue / 2)
                                pixels[target + 3] = byte.MaxValue;
                            else
                                pixels[target + 3] = (byte)(pixel[3] << 1);
                        }
                    }
                }
            }
            return ImageData.Create(info, PixelFormats.Bgra32, null, pixels); ;
        }
        public override void Write(Stream file, ImageData image)
        {
            throw new System.NotImplementedException("BipFormat.Write not implemented");
        }

        class BipImageMetaData : ImageMetaData
        {
            public uint BlockSize { get; set; }
            public bool Sliced { get; set; }
            public bool Multi { get; set; }
        }
    }
}