using System.Text;
using static System.MathF;

namespace Test
{
    public class Program
    {
        static void Main()
        {
            // 分别求得 色度空间 Y Cb Cr 的图层，分别是y_map, cb_map, cr_map;
            // 并且图层是补充过的，高和宽都被补足为8的整数倍，
            // 并且图层的像素值的范围已经被限定为【-128，127】
            // 其实可以使用并行算法提升编码速度。
            string image = @"C:\dell\test.bmp";
            Console.WriteLine("please write the file name here");
            var command = Console.ReadLine();
            image = command != null ? @"C:\dell\" + command + ".bmp" : image;

            // raw 是 bmp 里的每行要跳过的字节数，
            // raw_wieght 是要将图片的宽度补足成8的整数倍所需的字节数
            // raw_height 同上
            int width, height, raw, raw_width, raw_height; 

            // 一下三个是图片的rgb转ycbcr的三层色度空间的二维数据
            List<List<sbyte>> y_map, cb_map, cr_map;

            // 一下是读取数据， 处理图片数据的部分
            using (var stream = File.Open(image, FileMode.Open))
            {
                using (var reader = new BinaryReader(stream))
                {
                    stream.Position = 0x12;
                    
                    // 得出 宽度 和 高度
                    // 得出 raw  raw_weight raw_height
                    width = reader.ReadInt32();
                    height = reader.ReadInt32();
                    raw = (width * 3) % 4 == 0 ? 0 : 4 - (width * 3) % 4;
                    raw_width = width % 8 == 0 ? 0 : 8 - width % 8;
                    raw_height = height % 8 == 0 ? 0 : 8 - height % 8;

                    // 初始化一维
                    y_map = new List<List<sbyte>>(height + raw_height);
                    cb_map = new List<List<sbyte>>(height + raw_height);
                    cr_map = new List<List<sbyte>>(height + raw_height);

                    // 初始化二维 ， 注意 这个height, 而不是height + raw_height
                    for (int i = 0; i < height; i++)
                    {
                        y_map.Add(new List<sbyte>(width + raw_width));
                        cb_map.Add(new List<sbyte>(width + raw_width));
                        cr_map.Add(new List<sbyte>(width + raw_width));
                    }

                    // 开始读取处理图片的像素数据
                    stream.Position = 0x36;
                    for (int i = 0; i < height; i++)
                    {
                        for (int j = 0; j < width; j++)
                        {
                            byte[] rgb = reader.ReadBytes(3);                            
                            
                            // 获取rgb值
                            byte r = rgb[2], g = rgb[1], b = rgb[0];
                            sbyte y, cb, cr;
                            // 转化为ycbcr，然后填充进预置的格子里
                            y = (sbyte)(Round(0.299f * r + 0.587f * g + 0.114f * b) - 128);
                            cb = (sbyte)(Round(-0.1687f * r - 0.3313f * g + 0.5f * b));
                            cr = (sbyte)(Round(0.5f * r - 0.4187f * g - 0.0813f * b));
                            y_map[i].Add(y);
                            cb_map[i].Add(cb);
                            cr_map[i].Add(cr);
                        }

                        // 这部分是对于每行空余出的格子的默认补充， 默认为白色
                        for (int j = 0; j < raw_width; j++)
                        {
                            y_map[i].Add((sbyte)127);
                            cb_map[i].Add((sbyte)0);
                            cr_map[i].Add((sbyte)0);
                        }
                        if (i == height - 1) break;
                        
                        //跳过raw个冗余的字节
                        stream.Position += raw;
                    }
                }
            }

            //反转， 这是由于bmp的扫描顺序是自下而上， 自左而右，
            //上面的横向部分的格子已经填满， 但如果raw_height不为0，
            // 那么，仍有raw_HEIGHT 行需要填满
            // 由于我不想让这个默认填补的行出现在图片的顶部，
            //所以打算先反转，使得这个格子符合自上而下， 自左而右的顺序
            // 然后再去填充
            y_map.Reverse();
            cb_map.Reverse();
            cr_map.Reverse();

            // 填充空余的raw_height行格子
            for(int i = 0; i < raw_height; i++)
            {
                y_map.Add(new List<sbyte>(width + raw_width));
                cb_map.Add(new List<sbyte>(width + raw_width));
                cr_map.Add(new List<sbyte>(width + raw_width));
                var y_last = y_map.Last();
                var cb_last = cb_map.Last();
                var cr_last = cr_map.Last();
                for(int j = 0; j < width + raw_width; j++)
                {
                    y_last.Add((sbyte)127);
                    cb_last.Add((sbyte)(0));
                    cr_last.Add((sbyte)(0));
                }
            }

            // 分割成8*8小块, 对每块mcu进行DCT变换， 量化， zig-zag编码， 差分编码， 转化为二进制字符串，输入到StringPool里。

            // 设计为mcu的大小等于du，
            // 也就是mcu的大小为8*8， 每个格子的每个色度空间层都采样
            // width_num * height_num 也就是mcu的个数
            int width_num = (width + raw_width) / 8;
            int height_num = (height + raw_height) / 8;

            //这个是为了dc部分的差分编码用的
            sbyte y_pre = 0, cb_pre = 0, cr_pre = 0;

            // 开始分割处理
            for (int i = 0; i < height_num; i++)
            {
                for(int j = 0; j < width_num; j++)
                {
                    // 8*8的格子
                    sbyte[,] y_mcu = new sbyte[8, 8];
                    sbyte[,] cb_mcu = new sbyte[8, 8];
                    sbyte [,] cr_mcu = new sbyte[8, 8];

                    //填补格子
                    for(int m = i * 8, incre_m = 0; m < 8 + i * 8; m++, incre_m++)
                    {
                        // n 边8；了
                        for(int n = j * 8, incre_n = 0; n < 8 + j * 8; n++, incre_n++)
                        {
                            y_mcu[incre_m, incre_n] = y_map[m][n];
                            cb_mcu[incre_m, incre_n] = cb_map[m][n];
                            cr_mcu[incre_m, incre_n] = cr_map[m][n];
                        }
                    }

                    // 将8*8格子DTC 变换 ， 量化， 再zig-zag编码成一维序列
                    var list = ZigZag(DCT(y_mcu));
                    var list2 = ZigZag(DCT(cb_mcu, false));
                    var list3 = ZigZag(DCT(cr_mcu, false));

                    // 差分，将每个格子的dc值都设计成前一个的差值
                    sbyte y_temp = list[0];
                    list[0] -= y_pre;
                    y_pre = y_temp;

                    sbyte cb_temp = list2[0];
                    list2[0] -= cb_pre;
                    cb_pre = cb_temp;

                    sbyte cr_temp = list3[0];
                    list3[0] -= cr_pre;
                    cr_pre = cr_temp;
                    
                    
                    // 将序列解析为二进制字符串并入到StringPool中
                    ParseList(list, true);
                    ParseList(list2, false);
                    ParseList(list3, false);
                }

                // 这部分是为后面写入压缩数据准备的
                // 由于数据是以字节为单位储存的，
                // 所以将StringPool的长度补位8的整数倍，
                // 那样更方便解析
                long len = StringPool.Length;
                if(len%8 != 0)
                {
                    long fill = 8 - len%8;
                    for (int k = 0; k < fill; k++)
                        StringPool.Append('0');
                }
            }


            // write the new image whose formatt is jpeg.
            string new_image = @"C:\dell\tuya.jpg";
            using(var stream = File.OpenWrite(new_image))
            {
                using(var write = new BinaryWriter(stream))
                {

                    // 写入文件头和app0的数据
                    byte[] app0 = new byte[20]
                    {
                        0xff, 0xd8, 0xff, 0xe0, 0, 0x10,
                        0x4a, 0x46, 0x49, 0x46, 0,
                        1, 1, 1, 0, 0x78, 0, 0x78, 0, 0
                    };
                    write.Write(app0);

                    byte[] dqt_head_y = new byte[]
                    {
                        0xff, 0xdb, 0, 0x43, 0,
                    };

                    byte[] dqt_head_c = new byte[]
                    {
                        0xff, 0xdb, 0, 0x43, 1,
                    };

                    //写入y分量的量化表
                    write.Write(dqt_head_y);
                    for(int i = 0; i < 8; i++)
                    {
                        for(int j = 0; j < 8; j++)
                        {
                            write.Write(QT_Table_For_Y[i, j]);
                        }
                    }

                    //写入cb 和cr 的量化表
                    write.Write(dqt_head_c); 
                    for (int i = 0; i < 8; i++)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            write.Write(QT_Table_For_C[i, j]);
                        }
                    }

                    //写入扫描行数据
                    byte[] sof0 = new byte[0x13]
                    {
                        0xff, 0xc0, 0, 0x11, 8,
                        (byte)((height + raw_height)<<8),
                        (byte)((height + raw_height)%256),
                        (byte)((width + raw_width)<<8),
                        (byte)((width + raw_width)%256),
                        3, 1, 0x22, 0, 2, 0x22, 1, 3, 0x22, 1,
                    };
                    write.Write(sof0);

                    // dc for y
                    write.Write(new byte[5] { 0xff, 0xc4, 0, 0x1f, 0 });
                    write.Write(new byte[16]
                    {
                        0x00,0x01,0x05,0x01,0x01,0x01,0x01,0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    });
                    write.Write(new byte[12] 
                    {
                        0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0a,0x0b,
                    });

                    // ac for y
                    write.Write(new byte[5] { 0xff, 0xc4, 0, 0xb5, 0x10 });
                    write.Write(new byte[16]
                    {
                        0x00,0x02,0x01,0x03,0x03,0x02,0x04,0x03,0x05,0x05,0x04,0x04,0x00,0x00,0x01,0x7d,
                    });
                    write.Write(new byte[162]
                    {
                        0x01,0x02,0x03,0x00,0x04,0x11,0x05,0x12,0x21,0x31,0x41,0x06,0x13,0x51,0x61,0x07, /* HUFFVALS     */
                        0x22,0x71,0x14,0x32,0x81,0x91,0xa1,0x08,0x23,0x42,0xb1,0xc1,0x15,0x52,0xd1,0xf0,
                        0x24,0x33,0x62,0x72,0x82,0x09,0x0a,0x16,0x17,0x18,0x19,0x1a,0x25,0x26,0x27,0x28,
                        0x29,0x2a,0x34,0x35,0x36,0x37,0x38,0x39,0x3a,0x43,0x44,0x45,0x46,0x47,0x48,0x49,
                        0x4a,0x53,0x54,0x55,0x56,0x57,0x58,0x59,0x5a,0x63,0x64,0x65,0x66,0x67,0x68,0x69,
                        0x6a,0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7a,0x83,0x84,0x85,0x86,0x87,0x88,0x89,
                        0x8a,0x92,0x93,0x94,0x95,0x96,0x97,0x98,0x99,0x9a,0xa2,0xa3,0xa4,0xa5,0xa6,0xa7,
                        0xa8,0xa9,0xaa,0xb2,0xb3,0xb4,0xb5,0xb6,0xb7,0xb8,0xb9,0xba,0xc2,0xc3,0xc4,0xc5,
                        0xc6,0xc7,0xc8,0xc9,0xca,0xd2,0xd3,0xd4,0xd5,0xd6,0xd7,0xd8,0xd9,0xda,0xe1,0xe2,
                        0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xe9,0xea,0xf1,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf8,
                        0xf9,0xfa,
                    });

                    // dc for c
                    write.Write(new byte[5] { 0xff, 0xc4, 0, 0x1f, 0x01 });
                    write.Write(new byte[16]
                    {
                        0x00,0x03,0x01,0x01,0x01,0x01,0x01,0x01,0x01,0x01,0x01,0x00,0x00,0x00,0x00,0x00,
                    });
                    write.Write(new byte[12]
                    {
                        0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0a,0x0b,
                    });

                    // ac for c
                    write.Write(new byte[5] { 0xff, 0xc4, 0, 0xb5, 0x11 });
                    write.Write(new byte[16]
                    {
                        0x00,0x02,0x01,0x02,0x04,0x04,0x03,0x04,0x07,0x05,0x04,0x04,0x00,0x01,0x02,0x77,
                    });
                    write.Write(new byte[162]
                    {
                        0x00,0x01,0x02,0x03,0x11,0x04,0x05,0x21,0x31,0x06,0x12,0x41,0x51,0x07,0x61,0x71, /* HUFFVALS       */
                        0x13,0x22,0x32,0x81,0x08,0x14,0x42,0x91,0xa1,0xb1,0xc1,0x09,0x23,0x33,0x52,0xf0,
                        0x15,0x62,0x72,0xd1,0x0a,0x16,0x24,0x34,0xe1,0x25,0xf1,0x17,0x18,0x19,0x1a,0x26,
                        0x27,0x28,0x29,0x2a,0x35,0x36,0x37,0x38,0x39,0x3a,0x43,0x44,0x45,0x46,0x47,0x48,
                        0x49,0x4a,0x53,0x54,0x55,0x56,0x57,0x58,0x59,0x5a,0x63,0x64,0x65,0x66,0x67,0x68,
                        0x69,0x6a,0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7a,0x82,0x83,0x84,0x85,0x86,0x87,
                        0x88,0x89,0x8a,0x92,0x93,0x94,0x95,0x96,0x97,0x98,0x99,0x9a,0xa2,0xa3,0xa4,0xa5,
                        0xa6,0xa7,0xa8,0xa9,0xaa,0xb2,0xb3,0xb4,0xb5,0xb6,0xb7,0xb8,0xb9,0xba,0xc2,0xc3,
                        0xc4,0xc5,0xc6,0xc7,0xc8,0xc9,0xca,0xd2,0xd3,0xd4,0xd5,0xd6,0xd7,0xd8,0xd9,0xda,
                        0xe2,0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xe9,0xea,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf8,
                        0xf9,0xfa
                    });

                    // sos 
                    write.Write(new byte[14]
                    {
                        0xff, 0xda, 0, 0x0c, 0x03, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3f, 0
                    });

                    //data 开始写入压缩数据
                    long start_index = 0;
                    do
                    {
                        //提取八个字符以转化为byte值
                        string str = StringPool.ToString((int)start_index, 8);
                        byte num = Convert.ToByte(str, 2);
                        Console.WriteLine(Convert.ToString(num, 16));
                        // 写入字节值
                        write.Write(num);
                        // 如果写入的字节值是0xff， 那么需要再后面再写入0x00
                        if(num == 0xff)
                            write.Write((byte)0);
                        start_index += 8;
                    }while (start_index < StringPool.Length);

                    //eoi
                    write.Write(new byte[] { 0xff, 0xd9 });
                }
            }
        }

        static StringBuilder StringPool = new StringBuilder(4_000_000);

        //解析长度为64的序列的函数
        // b 参数控制对照的表示y分量的还是c分量的
        // 真，对照表为y， 否则为c
        static void ParseList(sbyte[] data, bool b)
        {
            // 自后确定eob的位置
            // eob的前一个值的索引
            int index_of_eob = 63;
            // 非零值前的零的个数
            byte zero_num = 0;
            while(index_of_eob > 0)
            {
                if (data[index_of_eob] == 0)
                    --index_of_eob;
                else
                    break;
            }

            // 开始自头读取解析
            // 前缀 和 字符串数据
            string prefix, str;
            for(int i = 0; i <= index_of_eob; ++i)
            {
                // dc
                if (i == 0)
                {
                    // 注意dc值是零的情况
                    if(data[i] == 0)
                    {
                        prefix = b ? DHT_DC_For_Y[0] : DHT_DC_For_C[0];
                        StringPool.Append(prefix);
                    }                   
                    else
                    {
                        str = TranslateForValue(data[i]);
                        prefix = b ? DHT_DC_For_Y[str.Length] : DHT_DC_For_C[str.Length];
                        StringPool.Append(prefix);
                        StringPool.Append(str);
                    }
                }
                //ac
                else
                {
                    //注意十六个值都为零的情况
                    if (data[i] == 0)
                    {
                        if (zero_num < 15)
                        {
                            zero_num++;
                            continue;
                        }
                        prefix = TranslateForAC(0xf0, b);
                        StringPool.Append(prefix);
                        zero_num = 0;
                    }
                    else
                    {
                        str = TranslateForValue(data[i]);
                        prefix = TranslateForAC((byte)(zero_num * 16 + str.Length), b);
                        StringPool.Append(prefix);
                        StringPool.Append(str);
                        zero_num = 0;
                    }
                }
            }
            //最后还有eob
            if (index_of_eob != 63) 
                StringPool.Append(TranslateForAC(0, b));
        }

        //将数字解析成二进制字符串形式，用于序列的元素
        static string TranslateForValue(sbyte num)
        {
            sbyte abs = (sbyte)Abs(num);
            string temp = Convert.ToString(abs, 2);
            if (num < 0)
                abs = (sbyte)~abs;
            string temp_next = Convert.ToString(abs, 2);
            return num > 0 ? temp : temp_next.Substring(temp_next.Length - temp.Length, temp.Length);
        }

        //处理步长和Huffman表的部分
        static string TranslateForAC(byte index, bool b = true)
        {
            ushort num = b ? DHT_AC_For_Y[index] : DHT_AC_For_C[index];
            string temp = Convert.ToString(num, 2);
            return num > 2 ? temp : temp.PadLeft(2, '0');
        }
        
        // DCT变换和量化
        static sbyte[,] DCT(sbyte[,] bs, bool b = true)
        {
            float[,] pool = new float[8,8];
            sbyte[,] end = new sbyte[8,8];
            for (int u = 0; u < 8; u++)
            {
                float constance_u = u == 0 ? Sqrt(0.125f) : 0.5f;
                for (int v = 0; v < 8; v++)
                {
                    float result = 0;
                    float constance_v = v == 0 ? Sqrt(0.125f) : 0.5f;
                    for (int i = 0; i < 8; i++)
                    {
                        float row_raw = Cos((i + 0.5f) * PI * u / 8);
                        float column_sum = 0;
                        //sbyte constance;
                        for (int j = 0; j < 8; j++)
                        {
                            sbyte constance = bs[i, j];
                            //byte qt_constace = b ? QT_Table_For_Y[i, j] : QT_Table_For_C[i, j];
                            float column_raw = Cos((j + 0.5f) * PI * v / 8);
                            column_sum += column_raw * constance;
                        }
                        result += row_raw * column_sum;
                    }
                    pool[u, v] = (result * constance_u * constance_v);
                }
            }
            
            //量化
            for (int u = 0; u < 8; u++)
            {
                for(int v = 0; v < 8; v++)
                {
                    byte qt = b ? QT_Table_For_Y[u, v] : QT_Table_For_C[u, v];
                    end[u, v] = (sbyte)Round(pool[u, v] / qt);
                }
            }
            return end;
        }

        // zig-zag编码函数
        static sbyte[] ZigZag(sbyte[,] shs)
        {
            sbyte[] pool = new sbyte[64];
            for(int i = 0; i < 8; i++)
            {
                for(int j = 0; j < 8; j++)
                {
                    pool[Zig_Zag_Table[i, j]] = shs[i, j];
                }
            }
            return pool;
        }

        // Y分量的了量化表
        static byte[,] QT_Table_For_Y = new byte[,] 
        {
            { 16, 11, 10, 16, 24, 40, 51, 61 },
            { 12, 12, 14, 19, 26, 58, 60, 55 },
            { 14, 13, 16, 24, 40, 57, 69, 56 },
            { 14, 17, 22, 29, 51, 87, 80, 62 },
            { 18, 22, 37, 56, 68, 109, 103, 77 },
            { 24, 35, 55, 64, 81, 104, 113, 92 },
            { 49, 64, 78, 87, 103, 121, 120, 101 },
            { 72, 92, 95, 98, 112, 100, 103, 99 }
        };

        // C分量的量化表
        static byte[,] QT_Table_For_C = new byte[,]
        {
            { 17, 18, 24, 47, 99, 99, 99, 99 },
            { 18, 21, 26, 66, 99, 99, 99, 99 },
            { 24, 26, 56, 99, 99, 99, 99, 99 },
            { 47, 66, 99, 99, 99, 99, 99, 99 },
            { 99, 99, 99, 99, 99, 99, 99, 99 },
            { 99, 99, 99, 99, 99, 99, 99, 99 },
            { 99, 99, 99, 99, 99, 99, 99, 99 },
            { 99, 99, 99, 99, 99, 99, 99, 99 },
        };

        //zig-zag编码需要的对照表
        static byte[,] Zig_Zag_Table =
        {
            { 0, 1, 5, 6, 14, 15, 27, 28 },
            { 2, 4, 7, 13, 16, 26, 29, 42  },
            { 3, 8, 12, 17, 25, 30, 41, 43 },
            { 9, 11, 18, 24, 31, 40, 44, 53 },
            { 10, 19, 23, 32, 39, 45, 52, 54 },
            { 20, 22, 33, 38, 46, 51, 55, 60 },
            { 21, 34, 37, 47, 50, 56, 59, 61 },
            { 35, 36, 48, 49, 57, 58, 62, 63 }
        };

        
        static string[] DHT_DC_For_C = new string[]
        {
            "00", "01", "10", "110", "1110", "11110",
            "111110", "1111110", "11111110", "111111110",
            "1111111110", "11111111110"
        };

        static string[] DHT_DC_For_Y = new string[]
        {
            "00", "010", "011", "100", "101", "110",
            "1110", "11110", "111110", "1111110",
            "11111110", "111111110"
        };

        static ushort[] DHT_AC_For_Y = new ushort[256]
        {
          0x000a, /* 0x00:              1010 */
          0x0000, /* 0x01:                00 */
          0x0001, /* 0x02:                01 */
          0x0004, /* 0x03:               100 */
          0x000b, /* 0x04:              1011 */
          0x001a, /* 0x05:             11010 */
          0x0078, /* 0x06:           1111000 */
          0x00f8, /* 0x07:          11111000 */
          0x03f6, /* 0x08:        1111110110 */
          0xff82, /* 0x09:  1111111110000010 */
          0xff83, /* 0x0a:  1111111110000011 */
          0x0000, /* 0x0b:                   */
          0x0000, /* 0x0c:                   */
          0x0000, /* 0x0d:                   */
          0x0000, /* 0x0e:                   */
          0x0000, /* 0x0f:                   */
          0x0000, /* 0x10:                   */
          0x000c, /* 0x11:              1100 */
          0x001b, /* 0x12:             11011 */
          0x0079, /* 0x13:           1111001 */
          0x01f6, /* 0x14:         111110110 */
          0x07f6, /* 0x15:       11111110110 */
          0xff84, /* 0x16:  1111111110000100 */
          0xff85, /* 0x17:  1111111110000101 */
          0xff86, /* 0x18:  1111111110000110 */
          0xff87, /* 0x19:  1111111110000111 */
          0xff88, /* 0x1a:  1111111110001000 */
          0x0000, /* 0x1b:                   */
          0x0000, /* 0x1c:                   */
          0x0000, /* 0x1d:                   */
          0x0000, /* 0x1e:                   */
          0x0000, /* 0x1f:                   */
          0x0000, /* 0x20:                   */
          0x001c, /* 0x21:             11100 */
          0x00f9, /* 0x22:          11111001 */
          0x03f7, /* 0x23:        1111110111 */
          0x0ff4, /* 0x24:      111111110100 */
          0xff89, /* 0x25:  1111111110001001 */
          0xff8a, /* 0x26:  1111111110001010 */
          0xff8b, /* 0x27:  1111111110001011 */
          0xff8c, /* 0x28:  1111111110001100 */
          0xff8d, /* 0x29:  1111111110001101 */
          0xff8e, /* 0x2a:  1111111110001110 */
          0x0000, /* 0x2b:                   */
          0x0000, /* 0x2c:                   */
          0x0000, /* 0x2d:                   */
          0x0000, /* 0x2e:                   */
          0x0000, /* 0x2f:                   */
          0x0000, /* 0x30:                   */
          0x003a, /* 0x31:            111010 */
          0x01f7, /* 0x32:         111110111 */
          0x0ff5, /* 0x33:      111111110101 */
          0xff8f, /* 0x34:  1111111110001111 */
          0xff90, /* 0x35:  1111111110010000 */
          0xff91, /* 0x36:  1111111110010001 */
          0xff92, /* 0x37:  1111111110010010 */
          0xff93, /* 0x38:  1111111110010011 */
          0xff94, /* 0x39:  1111111110010100 */
          0xff95, /* 0x3a:  1111111110010101 */
          0x0000, /* 0x3b:                   */
          0x0000, /* 0x3c:                   */
          0x0000, /* 0x3d:                   */
          0x0000, /* 0x3e:                   */
          0x0000, /* 0x3f:                   */
          0x0000, /* 0x40:                   */
          0x003b, /* 0x41:            111011 */
          0x03f8, /* 0x42:        1111111000 */
          0xff96, /* 0x43:  1111111110010110 */
          0xff97, /* 0x44:  1111111110010111 */
          0xff98, /* 0x45:  1111111110011000 */
          0xff99, /* 0x46:  1111111110011001 */
          0xff9a, /* 0x47:  1111111110011010 */
          0xff9b, /* 0x48:  1111111110011011 */
          0xff9c, /* 0x49:  1111111110011100 */
          0xff9d, /* 0x4a:  1111111110011101 */
          0x0000, /* 0x4b:                   */
          0x0000, /* 0x4c:                   */
          0x0000, /* 0x4d:                   */
          0x0000, /* 0x4e:                   */
          0x0000, /* 0x4f:                   */
          0x0000, /* 0x50:                   */
          0x007a, /* 0x51:           1111010 */
          0x07f7, /* 0x52:       11111110111 */
          0xff9e, /* 0x53:  1111111110011110 */
          0xff9f, /* 0x54:  1111111110011111 */
          0xffa0, /* 0x55:  1111111110100000 */
          0xffa1, /* 0x56:  1111111110100001 */
          0xffa2, /* 0x57:  1111111110100010 */
          0xffa3, /* 0x58:  1111111110100011 */
          0xffa4, /* 0x59:  1111111110100100 */
          0xffa5, /* 0x5a:  1111111110100101 */
          0x0000, /* 0x5b:                   */
          0x0000, /* 0x5c:                   */
          0x0000, /* 0x5d:                   */
          0x0000, /* 0x5e:                   */
          0x0000, /* 0x5f:                   */
          0x0000, /* 0x60:                   */
          0x007b, /* 0x61:           1111011 */
          0x0ff6, /* 0x62:      111111110110 */
          0xffa6, /* 0x63:  1111111110100110 */
          0xffa7, /* 0x64:  1111111110100111 */
          0xffa8, /* 0x65:  1111111110101000 */
          0xffa9, /* 0x66:  1111111110101001 */
          0xffaa, /* 0x67:  1111111110101010 */
          0xffab, /* 0x68:  1111111110101011 */
          0xffac, /* 0x69:  1111111110101100 */
          0xffad, /* 0x6a:  1111111110101101 */
          0x0000, /* 0x6b:                   */
          0x0000, /* 0x6c:                   */
          0x0000, /* 0x6d:                   */
          0x0000, /* 0x6e:                   */
          0x0000, /* 0x6f:                   */
          0x0000, /* 0x70:                   */
          0x00fa, /* 0x71:          11111010 */
          0x0ff7, /* 0x72:      111111110111 */
          0xffae, /* 0x73:  1111111110101110 */
          0xffaf, /* 0x74:  1111111110101111 */
          0xffb0, /* 0x75:  1111111110110000 */
          0xffb1, /* 0x76:  1111111110110001 */
          0xffb2, /* 0x77:  1111111110110010 */
          0xffb3, /* 0x78:  1111111110110011 */
          0xffb4, /* 0x79:  1111111110110100 */
          0xffb5, /* 0x7a:  1111111110110101 */
          0x0000, /* 0x7b:                   */
          0x0000, /* 0x7c:                   */
          0x0000, /* 0x7d:                   */
          0x0000, /* 0x7e:                   */
          0x0000, /* 0x7f:                   */
          0x0000, /* 0x80:                   */
          0x01f8, /* 0x81:         111111000 */
          0x7fc0, /* 0x82:   111111111000000 */
          0xffb6, /* 0x83:  1111111110110110 */
          0xffb7, /* 0x84:  1111111110110111 */
          0xffb8, /* 0x85:  1111111110111000 */
          0xffb9, /* 0x86:  1111111110111001 */
          0xffba, /* 0x87:  1111111110111010 */
          0xffbb, /* 0x88:  1111111110111011 */
          0xffbc, /* 0x89:  1111111110111100 */
          0xffbd, /* 0x8a:  1111111110111101 */
          0x0000, /* 0x8b:                   */
          0x0000, /* 0x8c:                   */
          0x0000, /* 0x8d:                   */
          0x0000, /* 0x8e:                   */
          0x0000, /* 0x8f:                   */
          0x0000, /* 0x90:                   */
          0x01f9, /* 0x91:         111111001 */
          0xffbe, /* 0x92:  1111111110111110 */
          0xffbf, /* 0x93:  1111111110111111 */
          0xffc0, /* 0x94:  1111111111000000 */
          0xffc1, /* 0x95:  1111111111000001 */
          0xffc2, /* 0x96:  1111111111000010 */
          0xffc3, /* 0x97:  1111111111000011 */
          0xffc4, /* 0x98:  1111111111000100 */
          0xffc5, /* 0x99:  1111111111000101 */
          0xffc6, /* 0x9a:  1111111111000110 */
          0x0000, /* 0x9b:                   */
          0x0000, /* 0x9c:                   */
          0x0000, /* 0x9d:                   */
          0x0000, /* 0x9e:                   */
          0x0000, /* 0x9f:                   */
          0x0000, /* 0xa0:                   */
          0x01fa, /* 0xa1:         111111010 */
          0xffc7, /* 0xa2:  1111111111000111 */
          0xffc8, /* 0xa3:  1111111111001000 */
          0xffc9, /* 0xa4:  1111111111001001 */
          0xffca, /* 0xa5:  1111111111001010 */
          0xffcb, /* 0xa6:  1111111111001011 */
          0xffcc, /* 0xa7:  1111111111001100 */
          0xffcd, /* 0xa8:  1111111111001101 */
          0xffce, /* 0xa9:  1111111111001110 */
          0xffcf, /* 0xaa:  1111111111001111 */
          0x0000, /* 0xab:                   */
          0x0000, /* 0xac:                   */
          0x0000, /* 0xad:                   */
          0x0000, /* 0xae:                   */
          0x0000, /* 0xaf:                   */
          0x0000, /* 0xb0:                   */
          0x03f9, /* 0xb1:        1111111001 */
          0xffd0, /* 0xb2:  1111111111010000 */
          0xffd1, /* 0xb3:  1111111111010001 */
          0xffd2, /* 0xb4:  1111111111010010 */
          0xffd3, /* 0xb5:  1111111111010011 */
          0xffd4, /* 0xb6:  1111111111010100 */
          0xffd5, /* 0xb7:  1111111111010101 */
          0xffd6, /* 0xb8:  1111111111010110 */
          0xffd7, /* 0xb9:  1111111111010111 */
          0xffd8, /* 0xba:  1111111111011000 */
          0x0000, /* 0xbb:                   */
          0x0000, /* 0xbc:                   */
          0x0000, /* 0xbd:                   */
          0x0000, /* 0xbe:                   */
          0x0000, /* 0xbf:                   */
          0x0000, /* 0xc0:                   */
          0x03fa, /* 0xc1:        1111111010 */
          0xffd9, /* 0xc2:  1111111111011001 */
          0xffda, /* 0xc3:  1111111111011010 */
          0xffdb, /* 0xc4:  1111111111011011 */
          0xffdc, /* 0xc5:  1111111111011100 */
          0xffdd, /* 0xc6:  1111111111011101 */
          0xffde, /* 0xc7:  1111111111011110 */
          0xffdf, /* 0xc8:  1111111111011111 */
          0xffe0, /* 0xc9:  1111111111100000 */
          0xffe1, /* 0xca:  1111111111100001 */
          0x0000, /* 0xcb:                   */
          0x0000, /* 0xcc:                   */
          0x0000, /* 0xcd:                   */
          0x0000, /* 0xce:                   */
          0x0000, /* 0xcf:                   */
          0x0000, /* 0xd0:                   */
          0x07f8, /* 0xd1:       11111111000 */
          0xffe2, /* 0xd2:  1111111111100010 */
          0xffe3, /* 0xd3:  1111111111100011 */
          0xffe4, /* 0xd4:  1111111111100100 */
          0xffe5, /* 0xd5:  1111111111100101 */
          0xffe6, /* 0xd6:  1111111111100110 */
          0xffe7, /* 0xd7:  1111111111100111 */
          0xffe8, /* 0xd8:  1111111111101000 */
          0xffe9, /* 0xd9:  1111111111101001 */
          0xffea, /* 0xda:  1111111111101010 */
          0x0000, /* 0xdb:                   */
          0x0000, /* 0xdc:                   */
          0x0000, /* 0xdd:                   */
          0x0000, /* 0xde:                   */
          0x0000, /* 0xdf:                   */
          0x0000, /* 0xe0:                   */
          0xffeb, /* 0xe1:  1111111111101011 */
          0xffec, /* 0xe2:  1111111111101100 */
          0xffed, /* 0xe3:  1111111111101101 */
          0xffee, /* 0xe4:  1111111111101110 */
          0xffef, /* 0xe5:  1111111111101111 */
          0xfff0, /* 0xe6:  1111111111110000 */
          0xfff1, /* 0xe7:  1111111111110001 */
          0xfff2, /* 0xe8:  1111111111110010 */
          0xfff3, /* 0xe9:  1111111111110011 */
          0xfff4, /* 0xea:  1111111111110100 */
          0x0000, /* 0xeb:                   */
          0x0000, /* 0xec:                   */
          0x0000, /* 0xed:                   */
          0x0000, /* 0xee:                   */
          0x0000, /* 0xef:                   */
          0x07f9, /* 0xf0:       11111111001 */
          0xfff5, /* 0xf1:  1111111111110101 */
          0xfff6, /* 0xf2:  1111111111110110 */
          0xfff7, /* 0xf3:  1111111111110111 */
          0xfff8, /* 0xf4:  1111111111111000 */
          0xfff9, /* 0xf5:  1111111111111001 */
          0xfffa, /* 0xf6:  1111111111111010 */
          0xfffb, /* 0xf7:  1111111111111011 */
          0xfffc, /* 0xf8:  1111111111111100 */
          0xfffd, /* 0xf9:  1111111111111101 */
          0xfffe, /* 0xfa:  1111111111111110 */
          0x0000, /* 0xfb:                   */
          0x0000, /* 0xfc:                   */
          0x0000, /* 0xfd:                   */
          0x0000, /* 0xfe:                   */
          0x0000, /* 0xff:                   */
        };

        static ushort[] DHT_AC_For_C = new ushort[256]
         {

          0x0000, /* 0x00:                00 */
          0x0001, /* 0x01:                01 */
          0x0004, /* 0x02:               100 */
          0x000a, /* 0x03:              1010 */
          0x0018, /* 0x04:             11000 */
          0x0019, /* 0x05:             11001 */
          0x0038, /* 0x06:            111000 */
          0x0078, /* 0x07:           1111000 */
          0x01f4, /* 0x08:         111110100 */
          0x03f6, /* 0x09:        1111110110 */
          0x0ff4, /* 0x0a:      111111110100 */
          0x0000, /* 0x0b:                   */
          0x0000, /* 0x0c:                   */
          0x0000, /* 0x0d:                   */
          0x0000, /* 0x0e:                   */
          0x0000, /* 0x0f:                   */
          0x0000, /* 0x10:                   */
          0x000b, /* 0x11:              1011 */
          0x0039, /* 0x12:            111001 */
          0x00f6, /* 0x13:          11110110 */
          0x01f5, /* 0x14:         111110101 */
          0x07f6, /* 0x15:       11111110110 */
          0x0ff5, /* 0x16:      111111110101 */
          0xff88, /* 0x17:  1111111110001000 */
          0xff89, /* 0x18:  1111111110001001 */
          0xff8a, /* 0x19:  1111111110001010 */
          0xff8b, /* 0x1a:  1111111110001011 */
          0x0000, /* 0x1b:                   */
          0x0000, /* 0x1c:                   */
          0x0000, /* 0x1d:                   */
          0x0000, /* 0x1e:                   */
          0x0000, /* 0x1f:                   */
          0x0000, /* 0x20:                   */
          0x001a, /* 0x21:             11010 */
          0x00f7, /* 0x22:          11110111 */
          0x03f7, /* 0x23:        1111110111 */
          0x0ff6, /* 0x24:      111111110110 */
          0x7fc2, /* 0x25:   111111111000010 */
          0xff8c, /* 0x26:  1111111110001100 */
          0xff8d, /* 0x27:  1111111110001101 */
          0xff8e, /* 0x28:  1111111110001110 */
          0xff8f, /* 0x29:  1111111110001111 */
          0xff90, /* 0x2a:  1111111110010000 */
          0x0000, /* 0x2b:                   */
          0x0000, /* 0x2c:                   */
          0x0000, /* 0x2d:                   */
          0x0000, /* 0x2e:                   */
          0x0000, /* 0x2f:                   */
          0x0000, /* 0x30:                   */
          0x001b, /* 0x31:             11011 */
          0x00f8, /* 0x32:          11111000 */
          0x03f8, /* 0x33:        1111111000 */
          0x0ff7, /* 0x34:      111111110111 */
          0xff91, /* 0x35:  1111111110010001 */
          0xff92, /* 0x36:  1111111110010010 */
          0xff93, /* 0x37:  1111111110010011 */
          0xff94, /* 0x38:  1111111110010100 */
          0xff95, /* 0x39:  1111111110010101 */
          0xff96, /* 0x3a:  1111111110010110 */
          0x0000, /* 0x3b:                   */
          0x0000, /* 0x3c:                   */
          0x0000, /* 0x3d:                   */
          0x0000, /* 0x3e:                   */
          0x0000, /* 0x3f:                   */
          0x0000, /* 0x40:                   */
          0x003a, /* 0x41:            111010 */
          0x01f6, /* 0x42:         111110110 */
          0xff97, /* 0x43:  1111111110010111 */
          0xff98, /* 0x44:  1111111110011000 */
          0xff99, /* 0x45:  1111111110011001 */
          0xff9a, /* 0x46:  1111111110011010 */
          0xff9b, /* 0x47:  1111111110011011 */
          0xff9c, /* 0x48:  1111111110011100 */
          0xff9d, /* 0x49:  1111111110011101 */
          0xff9e, /* 0x4a:  1111111110011110 */
          0x0000, /* 0x4b:                   */
          0x0000, /* 0x4c:                   */
          0x0000, /* 0x4d:                   */
          0x0000, /* 0x4e:                   */
          0x0000, /* 0x4f:                   */
          0x0000, /* 0x50:                   */
          0x003b, /* 0x51:            111011 */
          0x03f9, /* 0x52:        1111111001 */
          0xff9f, /* 0x53:  1111111110011111 */
          0xffa0, /* 0x54:  1111111110100000 */
          0xffa1, /* 0x55:  1111111110100001 */
          0xffa2, /* 0x56:  1111111110100010 */
          0xffa3, /* 0x57:  1111111110100011 */
          0xffa4, /* 0x58:  1111111110100100 */
          0xffa5, /* 0x59:  1111111110100101 */
          0xffa6, /* 0x5a:  1111111110100110 */
          0x0000, /* 0x5b:                   */
          0x0000, /* 0x5c:                   */
          0x0000, /* 0x5d:                   */
          0x0000, /* 0x5e:                   */
          0x0000, /* 0x5f:                   */
          0x0000, /* 0x60:                   */
          0x0079, /* 0x61:           1111001 */
          0x07f7, /* 0x62:       11111110111 */
          0xffa7, /* 0x63:  1111111110100111 */
          0xffa8, /* 0x64:  1111111110101000 */
          0xffa9, /* 0x65:  1111111110101001 */
          0xffaa, /* 0x66:  1111111110101010 */
          0xffab, /* 0x67:  1111111110101011 */
          0xffac, /* 0x68:  1111111110101100 */
          0xffad, /* 0x69:  1111111110101101 */
          0xffae, /* 0x6a:  1111111110101110 */
          0x0000, /* 0x6b:                   */
          0x0000, /* 0x6c:                   */
          0x0000, /* 0x6d:                   */
          0x0000, /* 0x6e:                   */
          0x0000, /* 0x6f:                   */
          0x0000, /* 0x70:                   */
          0x007a, /* 0x71:           1111010 */
          0x07f8, /* 0x72:       11111111000 */
          0xffaf, /* 0x73:  1111111110101111 */
          0xffb0, /* 0x74:  1111111110110000 */
          0xffb1, /* 0x75:  1111111110110001 */
          0xffb2, /* 0x76:  1111111110110010 */
          0xffb3, /* 0x77:  1111111110110011 */
          0xffb4, /* 0x78:  1111111110110100 */
          0xffb5, /* 0x79:  1111111110110101 */
          0xffb6, /* 0x7a:  1111111110110110 */
          0x0000, /* 0x7b:                   */
          0x0000, /* 0x7c:                   */
          0x0000, /* 0x7d:                   */
          0x0000, /* 0x7e:                   */
          0x0000, /* 0x7f:                   */
          0x0000, /* 0x80:                   */
          0x00f9, /* 0x81:          11111001 */
          0xffb7, /* 0x82:  1111111110110111 */
          0xffb8, /* 0x83:  1111111110111000 */
          0xffb9, /* 0x84:  1111111110111001 */
          0xffba, /* 0x85:  1111111110111010 */
          0xffbb, /* 0x86:  1111111110111011 */
          0xffbc, /* 0x87:  1111111110111100 */
          0xffbd, /* 0x88:  1111111110111101 */
          0xffbe, /* 0x89:  1111111110111110 */
          0xffbf, /* 0x8a:  1111111110111111 */
          0x0000, /* 0x8b:                   */
          0x0000, /* 0x8c:                   */
          0x0000, /* 0x8d:                   */
          0x0000, /* 0x8e:                   */
          0x0000, /* 0x8f:                   */
          0x0000, /* 0x90:                   */
          0x01f7, /* 0x91:         111110111 */
          0xffc0, /* 0x92:  1111111111000000 */
          0xffc1, /* 0x93:  1111111111000001 */
          0xffc2, /* 0x94:  1111111111000010 */
          0xffc3, /* 0x95:  1111111111000011 */
          0xffc4, /* 0x96:  1111111111000100 */
          0xffc5, /* 0x97:  1111111111000101 */
          0xffc6, /* 0x98:  1111111111000110 */
          0xffc7, /* 0x99:  1111111111000111 */
          0xffc8, /* 0x9a:  1111111111001000 */
          0x0000, /* 0x9b:                   */
          0x0000, /* 0x9c:                   */
          0x0000, /* 0x9d:                   */
          0x0000, /* 0x9e:                   */
          0x0000, /* 0x9f:                   */
          0x0000, /* 0xa0:                   */
          0x01f8, /* 0xa1:         111111000 */
          0xffc9, /* 0xa2:  1111111111001001 */
          0xffca, /* 0xa3:  1111111111001010 */
          0xffcb, /* 0xa4:  1111111111001011 */
          0xffcc, /* 0xa5:  1111111111001100 */
          0xffcd, /* 0xa6:  1111111111001101 */
          0xffce, /* 0xa7:  1111111111001110 */
          0xffcf, /* 0xa8:  1111111111001111 */
          0xffd0, /* 0xa9:  1111111111010000 */
          0xffd1, /* 0xaa:  1111111111010001 */
          0x0000, /* 0xab:                   */
          0x0000, /* 0xac:                   */
          0x0000, /* 0xad:                   */
          0x0000, /* 0xae:                   */
          0x0000, /* 0xaf:                   */
          0x0000, /* 0xb0:                   */
          0x01f9, /* 0xb1:         111111001 */
          0xffd2, /* 0xb2:  1111111111010010 */
          0xffd3, /* 0xb3:  1111111111010011 */
          0xffd4, /* 0xb4:  1111111111010100 */
          0xffd5, /* 0xb5:  1111111111010101 */
          0xffd6, /* 0xb6:  1111111111010110 */
          0xffd7, /* 0xb7:  1111111111010111 */
          0xffd8, /* 0xb8:  1111111111011000 */
          0xffd9, /* 0xb9:  1111111111011001 */
          0xffda, /* 0xba:  1111111111011010 */
          0x0000, /* 0xbb:                   */
          0x0000, /* 0xbc:                   */
          0x0000, /* 0xbd:                   */
          0x0000, /* 0xbe:                   */
          0x0000, /* 0xbf:                   */
          0x0000, /* 0xc0:                   */
          0x01fa, /* 0xc1:         111111010 */
          0xffdb, /* 0xc2:  1111111111011011 */
          0xffdc, /* 0xc3:  1111111111011100 */
          0xffdd, /* 0xc4:  1111111111011101 */
          0xffde, /* 0xc5:  1111111111011110 */
          0xffdf, /* 0xc6:  1111111111011111 */
          0xffe0, /* 0xc7:  1111111111100000 */
          0xffe1, /* 0xc8:  1111111111100001 */
          0xffe2, /* 0xc9:  1111111111100010 */
          0xffe3, /* 0xca:  1111111111100011 */
          0x0000, /* 0xcb:                   */
          0x0000, /* 0xcc:                   */
          0x0000, /* 0xcd:                   */
          0x0000, /* 0xce:                   */
          0x0000, /* 0xcf:                   */
          0x0000, /* 0xd0:                   */
          0x07f9, /* 0xd1:       11111111001 */
          0xffe4, /* 0xd2:  1111111111100100 */
          0xffe5, /* 0xd3:  1111111111100101 */
          0xffe6, /* 0xd4:  1111111111100110 */
          0xffe7, /* 0xd5:  1111111111100111 */
          0xffe8, /* 0xd6:  1111111111101000 */
          0xffe9, /* 0xd7:  1111111111101001 */
          0xffea, /* 0xd8:  1111111111101010 */
          0xffeb, /* 0xd9:  1111111111101011 */
          0xffec, /* 0xda:  1111111111101100 */
          0x0000, /* 0xdb:                   */
          0x0000, /* 0xdc:                   */
          0x0000, /* 0xdd:                   */
          0x0000, /* 0xde:                   */
          0x0000, /* 0xdf:                   */
          0x0000, /* 0xe0:                   */
          0x3fe0, /* 0xe1:    11111111100000 */
          0xffed, /* 0xe2:  1111111111101101 */
          0xffee, /* 0xe3:  1111111111101110 */
          0xffef, /* 0xe4:  1111111111101111 */
          0xfff0, /* 0xe5:  1111111111110000 */
          0xfff1, /* 0xe6:  1111111111110001 */
          0xfff2, /* 0xe7:  1111111111110010 */
          0xfff3, /* 0xe8:  1111111111110011 */
          0xfff4, /* 0xe9:  1111111111110100 */
          0xfff5, /* 0xea:  1111111111110101 */
          0x0000, /* 0xeb:                   */
          0x0000, /* 0xec:                   */
          0x0000, /* 0xed:                   */
          0x0000, /* 0xee:                   */
          0x0000, /* 0xef:                   */
          0x03fa, /* 0xf0:        1111111010 */
          0x7fc3, /* 0xf1:   111111111000011 */
          0xfff6, /* 0xf2:  1111111111110110 */
          0xfff7, /* 0xf3:  1111111111110111 */
          0xfff8, /* 0xf4:  1111111111111000 */
          0xfff9, /* 0xf5:  1111111111111001 */
          0xfffa, /* 0xf6:  1111111111111010 */
          0xfffb, /* 0xf7:  1111111111111011 */
          0xfffc, /* 0xf8:  1111111111111100 */
          0xfffd, /* 0xf9:  1111111111111101 */
          0xfffe, /* 0xfa:  1111111111111110 */
          0x0000, /* 0xfb:                   */
          0x0000, /* 0xfc:                   */
          0x0000, /* 0xfd:                   */
          0x0000, /* 0xfe:                   */
          0x0000  /* 0xff:                   */
        };
    }
}