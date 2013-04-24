using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.DirectX.DirectSound;
using Microsoft.DirectX;
using System.Timers;
using EricOulashin;

namespace Karaoke
{
    public partial class Form2 : Form
    {
        Form1 f;
        public Form2()
        {
            InitializeComponent();
        }

        public Form2(Form1 form)
        {
            f = form;
            InitializeComponent();
        }

        private string strRecSaveFile = string.Empty;//文件保存路径
        private Notify myNotify = null;//缓冲区提示事件
        private FileStream fsWav = null;//保存的文件流
        private int iNotifyNum = 16;//通知的个数
        private int iBufferOffset = 0;//本次数据起始点， 上一次数据的终点。
        private int iSampleSize = 0;//所采集到的数据大小
        private int iNotifySize = 0;//通知所在区域大小
        private int iBufferSize = 0;//缓冲区大小
        private BinaryWriter mWriter;
        private Capture capture = null;//捕捉设备对象
        private CaptureBuffer capturebuffer = null;//捕捉缓冲区
        private AutoResetEvent notifyevent = null;
        private Thread notifythread = null;
        private WaveFormat mWavFormat;//PCM格式

        private WaveFormat SetWaveFormat()
        {
            WaveFormat format = new WaveFormat();
            format.FormatTag = WaveFormatTag.Pcm;//设置音频类型
            format.SamplesPerSecond = 44100;//采样率（单位：赫兹）典型值：11025、22050、44100Hz
            format.BitsPerSample = 16;//采样位数
            format.Channels = 2;//声道
            format.BlockAlign = (short)(format.Channels * (format.BitsPerSample / 8));//单位采样点的字节数
            format.AverageBytesPerSecond = format.BlockAlign * format.SamplesPerSecond;
            return format;
            //按照以上采样规格，可知采样1秒钟的字节数为22050*2=55100B 约为 53K
        }

        private void CreateWaveFile(string strFileName)
        {
            fsWav = new FileStream(strFileName, FileMode.CreateNew);
            mWriter = new BinaryWriter(fsWav);
            /**************************************************************************
               Here is where the file will be created. A
               wave file is a RIFF file, which has chunks
               of data that describe what the file contains.
               A wave RIFF file is put together like this:
               The 12 byte RIFF chunk is constructed like this:
               Bytes 0 - 3 :  'R' 'I' 'F' 'F'
               Bytes 4 - 7 :  Length of file, minus the first 8 bytes of the RIFF description.
                                 (4 bytes for "WAVE" + 24 bytes for format chunk length +
                                 8 bytes for data chunk description + actual sample data size.)
                Bytes 8 - 11: 'W' 'A' 'V' 'E'
                The 24 byte FORMAT chunk is constructed like this:
                Bytes 0 - 3 : 'f' 'm' 't' ' '
                Bytes 4 - 7 : The format chunk length. This is always 16.
                Bytes 8 - 9 : File padding. Always 1.
                Bytes 10- 11: Number of channels. Either 1 for mono,  or 2 for stereo.
                Bytes 12- 15: Sample rate.
                Bytes 16- 19: Number of bytes per second.
                Bytes 20- 21: Bytes per sample. 1 for 8 bit mono, 2 for 8 bit stereo or
                                16 bit mono, 4 for 16 bit stereo.
                Bytes 22- 23: Number of bits per sample.
                The DATA chunk is constructed like this:
                Bytes 0 - 3 : 'd' 'a' 't' 'a'
                Bytes 4 - 7 : Length of data, in bytes.
                Bytes 8 -: Actual sample data.
              ***************************************************************************/
            char[] ChunkRiff = { 'R', 'I', 'F', 'F' };
            char[] ChunkType = { 'W', 'A', 'V', 'E' };
            char[] ChunkFmt = { 'f', 'm', 't', ' ' };
            char[] ChunkData = { 'd', 'a', 't', 'a' };
            short shPad = 1;                // File padding
            int nFormatChunkLength = 0x10;  // Format chunk length.
            int nLength = 0;                // File length, minus first 8 bytes of RIFF description. This will be filled in later.
            short shBytesPerSample = 0;     // Bytes per sample.
            // 一个样本点的字节数目
            if (8 == mWavFormat.BitsPerSample && 1 == mWavFormat.Channels)
                shBytesPerSample = 1;
            else if ((8 == mWavFormat.BitsPerSample && 2 == mWavFormat.Channels) || (16 == mWavFormat.BitsPerSample && 1 == mWavFormat.Channels))
                shBytesPerSample = 2;
            else if (16 == mWavFormat.BitsPerSample && 2 == mWavFormat.Channels)
                shBytesPerSample = 4;
            // RIFF 块
            mWriter.Write(ChunkRiff);
            mWriter.Write(nLength);
            mWriter.Write(ChunkType);
            // WAVE块
            mWriter.Write(ChunkFmt);
            mWriter.Write(nFormatChunkLength);
            mWriter.Write(shPad);
            mWriter.Write(mWavFormat.Channels);
            mWriter.Write(mWavFormat.SamplesPerSecond);
            mWriter.Write(mWavFormat.AverageBytesPerSecond);
            mWriter.Write(shBytesPerSample);
            mWriter.Write(mWavFormat.BitsPerSample);
            // 数据块
            mWriter.Write(ChunkData);
            mWriter.Write((int)0);   // The sample length will be written in later.
        }

        private bool CreateCaptuerDevice()
        {
            //首先要玫举可用的捕捉设备
            CaptureDevicesCollection capturedev = new CaptureDevicesCollection();
            Guid devguid;
            if (capturedev.Count > 0)
            {
                devguid = capturedev[0].DriverGuid;
            }
            else
            {
                MessageBox.Show("当前没有可用于音频捕捉的设备", "系统提示");
                return false;
            }
            //利用设备GUID来建立一个捕捉设备对象
            capture = new Capture(devguid);
            return true;
        }

        private void CreateCaptureBuffer()
        {//想要创建一个捕捉缓冲区必须要两个参数：缓冲区信息（描述这个缓冲区中的格式等），缓冲设备。

            CaptureBufferDescription bufferdescription = new CaptureBufferDescription();
            bufferdescription.Format = mWavFormat;//设置缓冲区要捕捉的数据格式
            iNotifySize = 1024;//设置通知大小
            iBufferSize = iNotifyNum * iNotifySize;
            bufferdescription.BufferBytes = iBufferSize;
            capturebuffer = new CaptureBuffer(bufferdescription, capture);//建立设备缓冲区对象
        }

        //设置通知
        private void CreateNotification()
        {
            BufferPositionNotify[] bpn = new BufferPositionNotify[iNotifyNum];//设置缓冲区通知个数
            //设置通知事件
            notifyevent = new AutoResetEvent(false);
            notifythread = new Thread(RecoData);
            notifythread.Start();
            for (int i = 0; i < iNotifyNum; i++)
            {
                bpn[i].Offset = iNotifySize + i * iNotifySize - 1;//设置具体每个的位置
                bpn[i].EventNotifyHandle = notifyevent.Handle;
            }
            myNotify = new Notify(capturebuffer);
            myNotify.SetNotificationPositions(bpn);

        }
        //线程中的事件
        private void RecoData()
        {
            while (true)
            {
                // 等待缓冲区的通知消息
                notifyevent.WaitOne(Timeout.Infinite, true);
                // 录制数据
                RecordCapturedData();
            }
        }

        //真正转移数据的事件，其实就是把数据转移到WAV文件中。
        private void RecordCapturedData()
        {
            byte[] capturedata = null;
            int readpos = 0, capturepos = 0, locksize = 0;
            capturebuffer.GetCurrentPosition(out capturepos, out readpos);
            locksize = readpos - iBufferOffset;//这个大小就是我们可以安全读取的大小
            if (locksize == 0)
            {
                return;
            }
            if (locksize < 0)
            {//因为我们是循环的使用缓冲区，所以有一种情况下为负：当文以载读指针回到第一个通知点，而Ibuffeoffset还在最后一个通知处
                locksize += iBufferSize;
            }

            capturedata = (byte[])capturebuffer.Read(iBufferOffset, typeof(byte), LockFlag.FromWriteCursor, locksize);
            mWriter.Write(capturedata, 0, capturedata.Length);//写入到文件
            iSampleSize += capturedata.Length;
            iBufferOffset += capturedata.Length;
            iBufferOffset %= iBufferSize;//取模是因为缓冲区是循环的。
        }

        private void stoprec()
        {
            capturebuffer.Stop();//调用缓冲区的停止方法。停止采集声音
            if (notifyevent != null)
                notifyevent.Set();//关闭通知
            notifythread.Abort();//结束线程
            RecordCapturedData();//将缓冲区最后一部分数据写入到文件中

            //写WAV文件尾
            mWriter.Seek(4, SeekOrigin.Begin);
            mWriter.Write((int)(iSampleSize + 36));   // 写文件长度
            mWriter.Seek(40, SeekOrigin.Begin);
            mWriter.Write(iSampleSize);                // 写数据长度
            mWriter.Close();
            fsWav.Close();
            mWriter = null;
            fsWav = null;

        }

        string file;
        Lyrics lrc;
        WaveInfo wi;
        public void ini()
        {
            this.Text = this.Tag.ToString();
            file = this.Text;
            mWavFormat = SetWaveFormat();
            try
            {
                f.Hide();
                FileInfo de = new FileInfo(Path.GetDirectoryName(this.Text) + "\\" + Path.GetFileNameWithoutExtension(this.Text) + "T.wav");
                if (de.Exists)
                    de.Delete();
                CreateWaveFile(Path.GetDirectoryName(this.Text) + "\\" + Path.GetFileNameWithoutExtension(this.Text) + "T.wav");
                CreateCaptuerDevice();
                CreateCaptureBuffer();
                CreateNotification();

                if (File.Exists(Path.GetDirectoryName(file) + "//" + Path.GetFileNameWithoutExtension(file) + ".lrc"))
                {
                    lrc = new Lyrics(file);
                }
                wi = new WaveInfo(file);
                progressBar1.Maximum = (int)wi.Second;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error");
                f.Show();
                this.Close();
            }

        }

        System.Media.SoundPlayer player;
        Thread th;
        Thread th2;

        private void button1_Click(object sender, EventArgs e)
        {
     //       this.axWindowsMediaPlayer1.URL = this.Text;
            
            player = new System.Media.SoundPlayer();
            player.SoundLocation = this.Text;
            player.Load();
            player.Play();
     
            capturebuffer.Start(true);
            if (File.Exists(Path.GetDirectoryName(file) + "//" + Path.GetFileNameWithoutExtension(file) + ".lrc"))
            {
                th = new Thread(new ThreadStart(DisplayLyrics));
                th.Start();
            }

            th2 = new Thread(new ThreadStart(SleepT));
            th2.Start(); 
     //       LyricTimer();
        }

        private delegate void DelegateWriteLyrics(string A, string B);

        private void WriteLyrics(string A, string B)
        {
            textBox1.Text = A;
            textBox2.Text = B;
            textBox1.Update();
            textBox2.Update();
        }


        private void DisplayLyrics()
        {
            this.Invoke(new DelegateWriteLyrics(WriteLyrics), new string[] {FindThis(0),FindNext(0)});
            if(lrc.tlrc[0].ms!=0)
                Thread.Sleep(lrc.tlrc[0].ms);
            if ((lrc.tlrc[1].ms-lrc.tlrc[0].ms) != 0)
                Thread.Sleep(lrc.tlrc[0].ms);
            int j = 1;
            while (j < lrc.tlrc.Length && !this.IsDisposed)
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new DelegateWriteLyrics(WriteLyrics), new string[] { FindThis(j),FindNext(j) });
                }
                if((lrc.tlrc[j+1].ms- lrc.tlrc[j].ms)!=0)
                {
                    Thread.Sleep(lrc.tlrc[j+1].ms - lrc.tlrc[j].ms);
                }
                j++;
            }
        }

        private string FindThis(int j)
        {
            while (lrc.tlrc[j].lrc.Trim() == "" && j<lrc.tlrc.Length)
                j++;
            return lrc.tlrc[j].lrc;
        }

        private string FindNext(int j)
        {
            while (lrc.tlrc[j].lrc.Trim() == "" && j < lrc.tlrc.Length)
                j++;
            if (j < lrc.tlrc.Length)
            {
                j++;
                while (lrc.tlrc[j].lrc.Trim() == "")
                    j++;
                return lrc.tlrc[j].lrc;
            }
            else
                return "";            
        }

        private delegate void DelegateStatus(string s);

        private void Status(string s)
        {
       //     f.label1.Text = s;
        }

        String[] audioFilenames;
        String fn;
        String tempDir;

        private void MergeWav()
        {
            WAVFile.MergeAudioFiles(audioFilenames, fn, tempDir);
        }

      
        private void button2_Click(object sender, EventArgs e)
        {
            player.Stop();
            stoprec();

            String userDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            userDir = userDir.Substring(0, userDir.LastIndexOf('\\'));
            tempDir = userDir + "\\AudioMergerTemp";
            Directory.CreateDirectory(tempDir);
            if (!Directory.Exists(tempDir))
                throw new SystemException("Unable to create temporary work directory: " + tempDir);

            OpenFileDialog fileDlg = new OpenFileDialog();
            fileDlg.Filter = "WAV audio files (*.wav)|*.wav";
            fileDlg.Title = "Choose an output file";
            fileDlg.CheckFileExists = false;
            if (fileDlg.ShowDialog() != DialogResult.OK)
            {
                Directory.Delete(tempDir);
                th.Abort();
                th2.Abort();
                this.Close();
                f.Show();
            }
            else
            {
                String mixOutputFilename = fileDlg.FileName;
                audioFilenames = new String[2] { this.Text, Path.GetDirectoryName(this.Text) + "\\" + Path.GetFileNameWithoutExtension(this.Text) + "T.wav" };
                fn = fileDlg.FileName;
                Thread th3 = new Thread(new ThreadStart(MergeWav));
                th3.Start();
                th3.Join();

                th.Abort();
                th2.Abort();

                FileInfo de = new FileInfo(Path.GetDirectoryName(this.Text) + "\\" + Path.GetFileNameWithoutExtension(this.Text) + "T.wav");
                de.Delete();

                this.Close();
                f.Show();
            }
        }

        private void axWindowsMediaPlayer1_Enter(object sender, EventArgs e)
        {

        }

        private delegate void SetPos(int ipos);

        private void SetTextMessage(int ipos)
        {
            if (this.InvokeRequired)
            {
                SetPos setpos = new SetPos(SetTextMessage);
                this.Invoke(setpos, new object[] { ipos });
            }
            else
            {
                this.label3.Text = ipos / 60 + ":" + ipos % 60 + "/" + ((int)wi.Second)/60 + ":" + ((int) wi.Second)%60;
                this.progressBar1.Value = Convert.ToInt32(ipos);
            }
        }

        private void SleepT()
        {
            for (int i = 0; i < 50000; i++)
            {
                System.Threading.Thread.Sleep(1000);
                SetTextMessage(i);
            }
        }

        private void Form2_FormClosing(object sender, FormClosingEventArgs e)
        {
            FileInfo de = new FileInfo(Path.GetDirectoryName(this.Text) + "\\" + Path.GetFileNameWithoutExtension(this.Text) + "T.wav");
            if (de.Exists)
                de.Delete();
        }
        /*
        private void MixWave(string A, string B,string des)
        {
            FileStream fs_1 = new FileStream(A, FileMode.Open, FileAccess.Read);
            FileStream fs_2 = new FileStream(B, FileMode.Open, FileAccess.Read);
            byte[] bInfo = new byte[56];
            fs_1.Read(bInfo, 0, 56);
            byte[] bInfo2 = new byte[56];
            fs_2.Read(bInfo2, 0, 56);
            //  byte[] filesize = System.BitConverter.GetBytes((int)wavinfo_1.filesize + (int)wavinfo_2.filesize);
            int file1size = System.BitConverter.ToInt32(bInfo, 4);
            int file2size = System.BitConverter.ToInt32(bInfo2, 4);
            byte[] filesize = System.BitConverter.GetBytes( (file1size>file2size)? file1size : file2size);
            byte[] datasize = System.BitConverter.GetBytes( ( System.BitConverter.ToInt32(bInfo, 52)>System.BitConverter.ToInt32(bInfo2, 52))? System.BitConverter.ToInt32(bInfo, 52): System.BitConverter.ToInt32(bInfo2, 52));
            FileStream tofile = new FileStream(des, FileMode.CreateNew);
            byte[] dushu_1 = new byte[file1size];
            byte[] dushu_2 = new byte[file2size];
            BinaryWriter w = new BinaryWriter(tofile);


            for (int i = 4, j = 0; i < 8; i++, j++)
            {
                bInfo[i] = filesize[j];
            }

            for (int i = 52, j = 0; i < 56; i++, j++)
            {
                bInfo[i] = datasize[j];
            }
            w.Write(bInfo);
            fs_1.Read(dushu_1, 0, file1size);
            fs_2.Read(dushu_2, 0, file2size);
            byte[]

            w.Write(dushu_2, 0, file2size);
            w.Flush();
            w.Close();
            fs_1.Close();
            fs_2.Close();
            tofile.Close();
        }

        */
    }

    class Lyrics
    {
        public struct TLrc
        {
            public int ms;//毫秒
            public string lrc;//对应的歌词
        }

        /// <summary>
        /// 标准的歌词结构数组
        /// </summary>
        public TLrc[] tlrc;
        /// <summary>
        /// 输入歌词文件路径处理歌词信息
        /// </summary>
        /// <param name="file"></param>
        public Lyrics(string file)
        {
            StreamReader sr = new StreamReader(getLrcFile(file), Encoding.Default);
            string[] lrc_1 = sr.ReadToEnd().Split(new char[] { '[', ']' });
            sr.Close();

            format_1(lrc_1);
            format_2(lrc_1);
            format_3();
        }
        /// <summary>
        /// 格式化不同时间相同字符如“[00:34.52][00:34.53][00:34.54]因为我已经没有力气”
        /// </summary>
        /// <param name="lrc_1"></param>
        private void format_1(string[] lrc_1)
        {
            for (int i = 2, j = 0; i < lrc_1.Length; i += 2, j = i)
            {
                while (lrc_1[j] == string.Empty)
                {
                    if (j + 2 < lrc_1.Length)
                    {
                        lrc_1[i] = lrc_1[j += 2];
                    }
                }
            }
        }
        /// <summary>
        /// 数据添加到结构体
        /// </summary>
        /// <param name="lrc_1"></param>
        private void format_2(string[] lrc_1)
        {
            tlrc = new TLrc[lrc_1.Length / 2];
            for (int i = 1, j = 0; i < lrc_1.Length; i++, j++)
            {
                tlrc[j].ms = timeToMs(lrc_1[i]);
                tlrc[j].lrc = lrc_1[++i];
            }
        }
        /// <summary>
        /// 时间格式”00:37.61“转毫秒
        /// </summary>
        /// <param name="lrc_t"></param>
        /// <returns></returns>
        private int timeToMs(string lrc_t)
        {
            float m, s, ms;
            string[] lrc_t_1 = lrc_t.Split(':');
            //这里不能用TryParse如“by:253057646”则有问题
            try
            {
                m = float.Parse(lrc_t_1[0]);
            }
            catch
            {
                return 0;
            }
            float.TryParse(lrc_t_1[1], out s);
            ms = m * 60000 + s * 1000;
            return (int)ms;
        }
        /// <summary>
        /// 排序，时间顺序
        /// </summary>
        private void format_3()
        {
            TLrc tlrc_temp;
            bool b = true;
            for (int i = 0; i < tlrc.Length - 1; i++, b = true)
            {
                for (int j = 0; j < tlrc.Length - i - 1; j++)
                {
                    if (tlrc[j].ms > tlrc[j + 1].ms)
                    {
                        tlrc_temp = tlrc[j];
                        tlrc[j] = tlrc[j + 1];
                        tlrc[j + 1] = tlrc_temp;
                        b = false;
                    }
                }
                if (b) break;
            }
        }

        public int mark;
        /// <summary>
        /// 读取下一条记录,并跳到下一条记录
        /// </summary>
        /// <returns></returns>
        public string ReadLine()
        {
            if (mark < tlrc.Length)
            {
                return tlrc[mark++].lrc;
            }
            else
            {
                return null;
            }

        }
        /// <summary>
        /// 读取当前行的歌词的时间
        /// </summary>
        /// <returns></returns>
        public int currentTime
        {
            get
            {
                if (mark < tlrc.Length)
                {
                    return tlrc[mark].ms;
                }
                else
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// 得到lrc歌词文件(当前目录)
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        private string getLrcFile(string file)
        {
            return Path.GetDirectoryName(file) + "//" + Path.GetFileNameWithoutExtension(file) + ".lrc";
        }
    }
    public class WaveInfo
    {
        /// <summary>
        /// 数据流
        /// </summary>
        private FileStream m_WaveData;

        private bool m_WaveBool = false;

        private RIFF_WAVE_Chunk _Header = new RIFF_WAVE_Chunk();
        private Format_Chunk _Format = new Format_Chunk();
        private Fact_Chunk _Fact = new Fact_Chunk();
        private Data_Chunk _Data = new Data_Chunk();
        public WaveInfo(string WaveFileName)
        {
            m_WaveData = new FileStream(WaveFileName, FileMode.Open);
            try
            {
                LoadWave();
                m_WaveData.Close();
            }
            catch
            {
                m_WaveData.Close();
            }
        }
        private void LoadWave()
        {
            #region RIFF_WAVE_Chunk
            byte[] _Temp4 = new byte[4];
            byte[] _Temp2 = new byte[2];
            m_WaveData.Read(_Temp4, 0, 4);
            if (_Temp4[0] != _Header.szRiffID[0] || _Temp4[1] != _Header.szRiffID[1] || _Temp4[2] != _Header.szRiffID[2] || _Temp4[3] != _Header.szRiffID[3]) return;
            m_WaveData.Read(_Temp4, 0, 4);
            _Header.dwRiffSize = BitConverter.ToUInt32(_Temp4, 0);
            m_WaveData.Read(_Temp4, 0, 4);
            if (_Temp4[0] != _Header.szRiffFormat[0] || _Temp4[1] != _Header.szRiffFormat[1] || _Temp4[2] != _Header.szRiffFormat[2] || _Temp4[3] != _Header.szRiffFormat[3]) return;

            #endregion
            #region Format_Chunk
            m_WaveData.Read(_Temp4, 0, 4);
            if (_Temp4[0] != _Format.ID[0] || _Temp4[1] != _Format.ID[1] || _Temp4[2] != _Format.ID[2]) return;
            m_WaveData.Read(_Temp4, 0, 4);
            _Format.Size = BitConverter.ToUInt32(_Temp4, 0);
            long _EndWave = _Format.Size + m_WaveData.Position;
            m_WaveData.Read(_Temp2, 0, 2);
            _Format.FormatTag = BitConverter.ToUInt16(_Temp2, 0);
            m_WaveData.Read(_Temp2, 0, 2);
            _Format.Channels = BitConverter.ToUInt16(_Temp2, 0);
            m_WaveData.Read(_Temp4, 0, 4);
            _Format.SamlesPerSec = BitConverter.ToUInt32(_Temp4, 0);
            m_WaveData.Read(_Temp4, 0, 4);
            _Format.AvgBytesPerSec = BitConverter.ToUInt32(_Temp4, 0);
            m_WaveData.Read(_Temp2, 0, 2);
            _Format.BlockAlign = BitConverter.ToUInt16(_Temp2, 0);
            m_WaveData.Read(_Temp2, 0, 2);
            _Format.BitsPerSample = BitConverter.ToUInt16(_Temp2, 0);
            m_WaveData.Position += _EndWave - m_WaveData.Position;
            #endregion
            m_WaveData.Read(_Temp4, 0, 4);
            if (_Temp4[0] == _Fact.ID[0] && _Temp4[1] == _Fact.ID[1] && _Temp4[2] == _Fact.ID[2] && _Temp4[3] == _Fact.ID[3])
            {
                #region  Fact_Chunk
                m_WaveData.Read(_Temp4, 0, 4);
                _Fact.Size = BitConverter.ToUInt32(_Temp4, 0);
                m_WaveData.Position += _Fact.Size;
                #endregion
                m_WaveData.Read(_Temp4, 0, 4);
            }
            if (_Temp4[0] == _Data.ID[0] && _Temp4[1] == _Data.ID[1] && _Temp4[2] == _Data.ID[2] && _Temp4[3] == _Data.ID[3])
            {
                #region Data_Chunk
                m_WaveData.Read(_Temp4, 0, 4);
                _Data.Size = BitConverter.ToUInt32(_Temp4, 0);
                _Data.FileBeginIndex = m_WaveData.Position;
                _Data.FileOverIndex = m_WaveData.Position + _Data.Size;
                m_Second = (double)_Data.Size / (double)_Format.AvgBytesPerSec;
                #endregion
            }

            m_WaveBool = true;
        }
        #region 文件定义
        /// <summary>
        /// 文件头
        /// </summary>
        private class RIFF_WAVE_Chunk
        {
            /// <summary>
            /// 文件前四个字节 为RIFF
            /// </summary>
            public byte[] szRiffID = new byte[] { 0x52, 0x49, 0x46, 0x46 };   // 'R','I','F','F'
            /// <summary>
            /// 数据大小 这个数字等于+8 =文件大小
            /// </summary>
            public uint dwRiffSize = 0;
            /// <summary>
            ///WAVE文件定义 为WAVE
            /// </summary>
            public byte[] szRiffFormat = new byte[] { 0x57, 0x41, 0x56, 0x45 }; // 'W','A','V','E'         
        }
        /// <summary>
        /// 声音内容定义
        /// </summary>
        private class Format_Chunk
        {
            /// <summary>
            /// 固定为  是"fmt "字后一位为0x20
            /// </summary>
            public byte[] ID = new byte[] { 0x66, 0x6D, 0x74, 0x20 };
            /// <summary>
            /// 区域大小
            /// </summary>
            public uint Size = 0;
            /// <summary>
            /// 记录着此声音的格式代号，例如1-WAVE_FORMAT_PCM， 2-WAVE_F0RAM_ADPCM等等。 
            /// </summary>
            public ushort FormatTag = 1;
            /// <summary>
            /// 声道数目，1--单声道；2--双声道
            /// </summary>
            public ushort Channels = 2;
            /// <summary>
            /// 采样频率  一般有11025Hz（11kHz）、22050Hz（22kHz）和44100Hz（44kHz）三种
            /// </summary>
            public uint SamlesPerSec = 0;
            /// <summary>
            /// 每秒所需字节数
            /// </summary>
            public uint AvgBytesPerSec = 0;
            /// <summary>
            /// 数据块对齐单位(每个采样需要的字节数)
            /// </summary>
            public ushort BlockAlign = 0;
            /// <summary>
            /// 音频采样大小 
            /// </summary>
            public ushort BitsPerSample = 0;
            /// <summary>
            /// ???
            /// </summary>
            public byte[] Temp = new byte[2];
        }
        /// <summary>
        /// FACT
        /// </summary>
        private class Fact_Chunk
        {
            /// <summary>
            /// 文件前四个字节 为fact
            /// </summary>
            public byte[] ID = new byte[] { 0x66, 0x61, 0x63, 0x74 };   // 'f','a','c','t'
            /// <summary>
            /// 数据大小
            /// </summary>
            public uint Size = 0;
            /// <summary>
            /// 临时数据
            /// </summary>
            public byte[] Temp;
        }
        /// <summary>
        /// 数据区
        /// </summary>
        private class Data_Chunk
        {
            /// <summary>
            /// 文件前四个字节 为RIFF
            /// </summary>
            public byte[] ID = new byte[] { 0x64, 0x61, 0x74, 0x61 };   // 'd','a','t','a'
            /// <summary>
            /// 大小
            /// </summary>
            public uint Size = 0;
            /// <summary>
            /// 开始播放的位置
            /// </summary>
            public long FileBeginIndex = 0;
            /// <summary>
            /// 结束播放的位置
            /// </summary>
            public long FileOverIndex = 0;
        }
        #endregion
        #region 属性
        /// <summary>
        /// 是否成功打开文件
        /// </summary>
        public bool WaveBool { get { return m_WaveBool; } }
        private double m_Second = 0;
        /// <summary>
        /// 秒单位
        /// </summary>
        public double Second { get { return m_Second; } }
        /// <summary>
        /// 格式
        /// </summary>
        public string FormatTag
        {
            get
            {
                switch (_Format.FormatTag)
                {
                    case 1:
                        return "PCM";
                    case 2:
                        return "Microsoft ADPCM";
                    default:
                        return "Un";
                }
            }
        }
        /// <summary>
        /// 频道
        /// </summary>
        public ushort Channels { get { return _Format.Channels; } }
        /// <summary>
        /// 采样级别
        /// </summary>
        public string SamlesPerSec
        {
            get
            {
                switch (_Format.SamlesPerSec)
                {
                    case 11025:
                        return "11kHz";
                    case 22050:
                        return "22kHz";
                    case 44100:
                        return "44kHz";
                    default:
                        return _Format.SamlesPerSec.ToString() + "Hz";
                }
            }
        }
        /// <summary>
        /// 采样大小
        /// </summary>
        public ushort BitsPerSample { get { return _Format.BitsPerSample; } }
        #endregion
    }
}
