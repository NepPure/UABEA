using AssetsTools.NET;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UABEAvalonia;
using Image = SixLabors.ImageSharp.Image;

namespace TexturePlugin
{
    public class EditTexture
    {
        private TextureFile tex;
        private AssetTypeValueField baseField;

        private byte[]? modImageBytes;


        public EditTexture(TextureFile tex, AssetTypeValueField baseField)
        {
            this.tex = tex;
            this.baseField = baseField;

            modImageBytes = null;
        }

        public bool Save(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (modImageBytes != null)
            {
                TextureFormat fmt = (TextureFormat)tex.m_TextureFormat;
                byte[] encImageBytes = TextureEncoderDecoder.Encode(modImageBytes, tex.m_Width, tex.m_Height, fmt);

                if (encImageBytes == null)
                {
                    return false;
                }

                AssetTypeValueField image_data = baseField.Get("image data");
                image_data.GetValue().type = EnumValueTypes.ByteArray;
                image_data.templateField.valueType = EnumValueTypes.ByteArray;
                AssetTypeByteArray byteArray = new AssetTypeByteArray()
                {
                    size = (uint)encImageBytes.Length,
                    data = encImageBytes
                };
                image_data.GetValue().Set(byteArray);
                return true;
            }
            else
            {
                return false;
            }
        }


        public void ImportTexture(string file)
        {
            using (Image<Rgba32> image = Image.Load<Rgba32>(file))
            {
                tex.m_Width = image.Width;
                tex.m_Height = image.Height;

                image.Mutate(i => i.Flip(FlipMode.Vertical));

                modImageBytes = new byte[tex.m_Width * tex.m_Height * 4];
                image.CopyPixelDataTo(modImageBytes);
            }
        }
    }

}
