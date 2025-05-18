using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace VirtualFileSystemApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new VFSForm());
        }
    }

    public class VFSForm : Form
    {
        private ListBox fileList = new ListBox();
        private Button btnCreate = new Button();
        private Button btnView = new Button();
        private Button btnModify = new Button();
        private Button btnDelete = new Button();
        private Button btnExport = new Button();
        private Button btnImport = new Button();
        private Button btnShowStatus = new Button();

        private VirtualFileSystem vfs;

        public VFSForm()
        {
            Text = "Virtual File System";
            Width = 500;
            Height = 450;

            fileList.Top = 10;
            fileList.Left = 10;
            fileList.Width = 460;
            fileList.Height = 200;

            btnCreate.Text = "Create";
            btnCreate.Top = 220;
            btnCreate.Left = 10;
            btnCreate.Click += (s, e) => CreateFile();

            btnView.Text = "View";
            btnView.Top = 220;
            btnView.Left = 100;
            btnView.Click += (s, e) => ViewFile();

            btnModify.Text = "Modify";
            btnModify.Top = 220;
            btnModify.Left = 190;
            btnModify.Click += (s, e) => ModifyFile();

            btnDelete.Text = "Delete";
            btnDelete.Top = 220;
            btnDelete.Left = 280;
            btnDelete.Click += (s, e) => DeleteFile();

            btnExport.Text = "Export";
            btnExport.Top = 220;
            btnExport.Left = 370;
            btnExport.Click += (s, e) => ExportFile();

            btnImport.Text = "Import";
            btnImport.Top = 250;
            btnImport.Left = 10;
            btnImport.Click += (s, e) => ImportFile();

            btnShowStatus.Text = "Show FS Status";
            btnShowStatus.Top = 250;
            btnShowStatus.Left = 100;
            btnShowStatus.Click += (s, e) => ShowFileSystemStatus();

            Controls.Add(fileList);
            Controls.Add(btnCreate);
            Controls.Add(btnView);
            Controls.Add(btnModify);
            Controls.Add(btnDelete);
            Controls.Add(btnExport);
            Controls.Add(btnImport);
            Controls.Add(btnShowStatus);

            FormClosing += (s, e) => vfs.SaveToDisk();

            vfs = new VirtualFileSystem("simpledisk.bin");
            RefreshFileList();
        }

        private void RefreshFileList()
        {
            fileList.Items.Clear();
            foreach (var name in vfs.GetFileNames())
                fileList.Items.Add(name);
        }

        private void CreateFile()
        {
            string name = Prompt("File name:");
            if (string.IsNullOrWhiteSpace(name)) return;

            string content = PromptMultiline("Enter file content:");
            if (vfs.CreateFile(name, content))
                RefreshFileList();
            else
                MessageBox.Show("Failed to create file.");
        }

        private void ViewFile()
        {
            if (fileList.SelectedItem == null) return;
            string name = fileList.SelectedItem.ToString();
            string content = vfs.ReadFile(name);
            MessageBox.Show(content, $"Contents of {name}");
        }

        private void ModifyFile()
        {
            if (fileList.SelectedItem == null) return;
            string name = fileList.SelectedItem.ToString();
            string content = PromptMultiline("Append content:");
            if (vfs.ModifyFile(name, content))
                MessageBox.Show("Modified.");
        }

        private void DeleteFile()
        {
            if (fileList.SelectedItem == null) return;
            string name = fileList.SelectedItem.ToString();
            if (vfs.DeleteFile(name))
            {
                MessageBox.Show("Deleted.");
                RefreshFileList();
            }
        }

        private void ExportFile()
        {
            if (fileList.SelectedItem == null) return;
            string name = fileList.SelectedItem.ToString();
            string content = vfs.ReadFile(name);

            using SaveFileDialog dlg = new SaveFileDialog
            {
                FileName = name,
                Filter = "Text files (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, content);
                MessageBox.Show("Exported.");
            }
        }

        private void ImportFile()
        {
            using OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                string fileName = Path.GetFileName(dlg.FileName);
                string content = File.ReadAllText(dlg.FileName);

                if (vfs.CreateFile(fileName, content))
                {
                    MessageBox.Show("File imported successfully.");
                    RefreshFileList();
                }
                else
                {
                    MessageBox.Show("Failed to import file.");
                }
            }
        }

        private void ShowFileSystemStatus()
        {
            string status = vfs.GetFileSystemStatus();
            MessageBox.Show(status, "File System Status");
        }

        private string Prompt(string title)
        {
            return Microsoft.VisualBasic.Interaction.InputBox(title, "Input");
        }

        private string PromptMultiline(string title)
        {
            using var form = new Form { Width = 400, Height = 300, Text = title };
            var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            var ok = new Button { Text = "OK", Dock = DockStyle.Bottom };
            ok.Click += (s, e) => form.DialogResult = DialogResult.OK;
            form.Controls.Add(box);
            form.Controls.Add(ok);
            return form.ShowDialog() == DialogResult.OK ? box.Text : "";
        }
    }

    public class VirtualFileSystem
    {
        private const int TotalSize = 10 * 1024 * 1024;
        private const int DirSectionSize = 1 * 1024 * 1024;
        private const int FreeListSize = 1 * 1024 * 1024;
        private const int DataSectionSize = 8 * 1024 * 1024;
        private const int BlockSize = 1024;
        private const int MaxBlocks = DataSectionSize / BlockSize;
        private const int MaxFiles = DirSectionSize / 500;

        private byte[] storage;
        private bool[] freeBlocks;
        private List<int> freeBlockList;
        private int freeBlockCount;

        private string diskFileName;

        private class Entry
        {
            public string FileName = "";
            public int StartBlock = -1;
            public int FileSize = 0;
            public bool IsValid = false;
        }

        private Entry[] directory;

        public VirtualFileSystem(string filename)
        {
            diskFileName = filename;
            storage = new byte[TotalSize];
            freeBlocks = new bool[MaxBlocks];
            freeBlockList = new();
            directory = new Entry[MaxFiles];

            for (int i = 0; i < MaxFiles; i++)
                directory[i] = new Entry();

            for (int i = 0; i < MaxBlocks; i++)
            {
                freeBlocks[i] = true;
                freeBlockList.Add(i);
            }

            freeBlockCount = MaxBlocks;

            if (!LoadFromDisk())
                InitializeFileSystem();
        }

        private void InitializeFileSystem()
        {
            Array.Clear(storage, 0, storage.Length);
            UpdateFreeBlockList();
        }

        private int GetDataSectionStart() => DirSectionSize + FreeListSize;
        private int GetBlockAddress(int index) => GetDataSectionStart() + index * BlockSize;

        private void UpdateFreeBlockList()
        {
            int baseAddr = DirSectionSize;
            Array.Copy(BitConverter.GetBytes(freeBlockCount), 0, storage, baseAddr, 4);
            for (int i = 0; i < MaxBlocks; i++)
                storage[baseAddr + 4 + i] = (byte)(freeBlocks[i] ? 1 : 0);
        }

        private int AllocateBlock()
        {
            if (freeBlockList.Count == 0) return -1;
            int index = freeBlockList[0];
            freeBlockList.RemoveAt(0);
            freeBlocks[index] = false;
            freeBlockCount--;
            UpdateFreeBlockList();
            return index;
        }

        private void FreeBlock(int index)
        {
            if (index < 0 || index >= MaxBlocks || freeBlocks[index]) return;
            freeBlocks[index] = true;
            freeBlockList.Add(index);
            freeBlockCount++;
            Array.Clear(storage, GetBlockAddress(index), BlockSize);
            UpdateFreeBlockList();
        }

        public List<string> GetFileNames() =>
            directory.Where(d => d.IsValid).Select(d => d.FileName).ToList();

        public bool CreateFile(string name, string content)
        {
            if (directory.Any(d => d.IsValid && d.FileName == name)) return false;

            int dirIndex = Array.FindIndex(directory, d => !d.IsValid);
            if (dirIndex == -1) return false;

            byte[] data = Encoding.UTF8.GetBytes(content + "\0");
            int blocksNeeded = (data.Length + BlockSize - 5) / (BlockSize - 4);
            if (blocksNeeded > freeBlockCount) return false;

            List<int> blocks = new();
            for (int i = 0; i < blocksNeeded; i++)
            {
                int b = AllocateBlock();
                if (b == -1) { blocks.ForEach(FreeBlock); return false; }
                blocks.Add(b);
            }

            int written = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                int addr = GetBlockAddress(blocks[i]);
                int next = (i == blocks.Count - 1) ? -1 : blocks[i + 1];
                Array.Copy(BitConverter.GetBytes(next), 0, storage, addr, 4);
                int len = Math.Min(BlockSize - 4, data.Length - written);
                Array.Copy(data, written, storage, addr + 4, len);
                written += len;
            }

            directory[dirIndex].FileName = name;
            directory[dirIndex].StartBlock = blocks[0];
            directory[dirIndex].FileSize = data.Length;
            directory[dirIndex].IsValid = true;
            return true;
        }

        public bool DeleteFile(string name)
        {
            var entry = directory.FirstOrDefault(d => d.IsValid && d.FileName == name);
            if (entry == null) return false;
            int block = entry.StartBlock;
            while (block != -1)
            {
                int addr = GetBlockAddress(block);
                int next = BitConverter.ToInt32(storage, addr);
                FreeBlock(block);
                block = next;
            }
            entry.IsValid = false;
            return true;
        }

        public string ReadFile(string name)
        {
            var entry = directory.FirstOrDefault(d => d.IsValid && d.FileName == name);
            if (entry == null) return "";
            List<byte> content = new();
            int block = entry.StartBlock;
            while (block != -1)
            {
                int addr = GetBlockAddress(block);
                int next = BitConverter.ToInt32(storage, addr);
                for (int i = 4; i < BlockSize; i++)
                {
                    byte b = storage[addr + i];
                    if (b == 0) break;
                    content.Add(b);
                }
                block = next;
            }
            return Encoding.UTF8.GetString(content.ToArray());
        }

        public bool ModifyFile(string name, string newContent)
        {
            string old = ReadFile(name);
            if (string.IsNullOrEmpty(old)) return false;
            DeleteFile(name);
            return CreateFile(name, old + newContent);
        }

        public bool SaveToDisk()
        {
            try { File.WriteAllBytes(diskFileName, storage); return true; }
            catch { return false; }
        }

        public bool LoadFromDisk()
        {
            if (!File.Exists(diskFileName)) return false;
            try
            {
                storage = File.ReadAllBytes(diskFileName);
                int baseAddr = DirSectionSize;
                freeBlockCount = BitConverter.ToInt32(storage, baseAddr);
                freeBlockList.Clear();
                for (int i = 0; i < MaxBlocks; i++)
                {
                    bool free = storage[baseAddr + 4 + i] == 1;
                    freeBlocks[i] = free;
                    if (free) freeBlockList.Add(i);
                }
                return true;
            }
            catch { return false; }
        }

        // New method to show the file system status
        public string GetFileSystemStatus()
        {
            int usedBlocks = MaxBlocks - freeBlockCount;
            int usedFiles = directory.Count(d => d.IsValid);
            return $"File System Status:\n" +
                   $"Total Size: {TotalSize / 1024 / 1024} MB\n" +
                   $"Used Blocks: {usedBlocks} / {MaxBlocks}\n" +
                   $"Free Blocks: {freeBlockCount} / {MaxBlocks}\n" +
                   $"Used Files: {usedFiles} / {MaxFiles}\n";
        }
    }
}
