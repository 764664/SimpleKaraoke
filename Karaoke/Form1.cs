using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Collections;

namespace Karaoke
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            label1.Text = "Ready";
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem != null)
            {
                Form2 fm2 = new Form2(this);
                fm2.Tag = listBox1.SelectedItem.ToString();
                fm2.ini();
           //     this.Hide();
                fm2.ShowDialog(this);
          //      if (fm2.ShowDialog(this) == DialogResult.Cancel)
           //     {
            //        this.Show();
           //     }
            }
            else
            {
                MessageBox.Show("You must choose a song");
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog(); 
            openFileDialog.Filter = "Wave Files (*.wav)|*.wav";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string FileName in openFileDialog.FileNames)
                {
                    //   string FileName = openFileDialog.FileName;
                    string[] music = { /*".mp3", ".m4a", ".ogg", ".flac", ".ape", ".wma",*/".wav" };
                    if (((IList)music).Contains(Path.GetExtension(FileName)))
                        listBox1.Items.Add(FileName);
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            listBox1.Items.Remove(listBox1.SelectedItem);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();  
        }

        private void listBox1_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void listBox1_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void listBox1_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
                foreach (String s in files)
                {
                    if (Path.GetExtension(s) == ".wav")
                    {
                        (sender as ListBox).Items.Add(s);
                    }
                }
            }
        }
    }
}
