using ICSharpCode.TextEditor;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            textEditorControl1 = new TextEditorControl();
            treeView1 = new TreeView();
            richTextBox1 = new RichTextBox();
            pictureBox1 = new PictureBox();
            splitContainer1 = new SplitContainer();
            tableLayoutPanel1 = new TableLayoutPanel();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            openmetFileToolStripMenuItem = new ToolStripMenuItem();
            saveFileToPCToolStripMenuItem = new ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // textEditorControl1
            // 
            tableLayoutPanel1.SetColumnSpan(textEditorControl1, 2);
            textEditorControl1.Dock = DockStyle.Fill;
            textEditorControl1.IsReadOnly = false;
            textEditorControl1.Location = new Point(3, 3);
            textEditorControl1.Name = "textEditorControl1";
            textEditorControl1.Size = new Size(747, 410);
            textEditorControl1.Padding = new Padding(0, 0, 0, 5);
            textEditorControl1.TabIndex = 0;
            // 
            // treeView1
            // 
            treeView1.Dock = DockStyle.Fill;
            treeView1.Location = new Point(8, 0);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(362, 620);
            treeView1.TabIndex = 0;
            treeView1.BeforeExpand += treeView1_BeforeExpand;
            treeView1.AfterSelect += treeView1_AfterSelect;
            // 
            // richTextBox1
            // 
            richTextBox1.Dock = DockStyle.Fill;
            richTextBox1.Location = new Point(3, 419);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.ReadOnly = true;
            richTextBox1.Size = new Size(445, 203);
            richTextBox1.TabIndex = 2;
            richTextBox1.BackColor = Color.White;
            richTextBox1.Padding = new Padding(0, 0, 5, 5);
            richTextBox1.Text = "";
            // 
            // pictureBox1
            // 
            pictureBox1.BackColor = Color.White;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(454, 419);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(296, 203);
            pictureBox1.Padding = new Padding(0, 0, 0, 5);
            pictureBox1.TabIndex = 4;
            pictureBox1.TabStop = false;
            // 
            // splitContainer1
            // 
            splitContainer1.BackColor = SystemColors.Control;
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 28);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.BackColor = SystemColors.Control;
            splitContainer1.Panel1.Controls.Add(treeView1);
            splitContainer1.Panel1.Padding = new Padding(8, 0, 0, 5);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(tableLayoutPanel1);
            splitContainer1.Size = new Size(1127, 625);
            splitContainer1.SplitterDistance = 370;
            splitContainer1.TabIndex = 6;
            splitContainer1.Panel2.Padding = new Padding(0, 0, 5, 5);
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.BackColor = SystemColors.Control;
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            tableLayoutPanel1.Controls.Add(textEditorControl1, 0, 0);
            tableLayoutPanel1.Controls.Add(richTextBox1, 0, 1);
            tableLayoutPanel1.Controls.Add(pictureBox1, 1, 1);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 66.66666F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel1.Size = new Size(753, 625);
            tableLayoutPanel1.TabIndex = 5;
            // 
            // menuStrip1
            // 
            menuStrip1.Font = new Font("Segoe UI", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(1127, 28);
            menuStrip1.TabIndex = 7;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openmetFileToolStripMenuItem, saveFileToPCToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(44, 24);
            fileToolStripMenuItem.Text = "File";
            // 
            // openmetFileToolStripMenuItem
            // 
            openmetFileToolStripMenuItem.Name = "openmetFileToolStripMenuItem";
            openmetFileToolStripMenuItem.Size = new Size(177, 24);
            openmetFileToolStripMenuItem.Text = "Open .met file";
            openmetFileToolStripMenuItem.Click += openmetFileToolStripMenuItem_Click;
            // 
            // saveFileToPCToolStripMenuItem
            // 
            saveFileToPCToolStripMenuItem.Name = "saveFileToPCToolStripMenuItem";
            saveFileToPCToolStripMenuItem.Size = new Size(177, 24);
            saveFileToPCToolStripMenuItem.Text = "Save File To PC";
            saveFileToPCToolStripMenuItem.Click += saveFileToPCToolStripMenuItem_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(1127, 653);
            Controls.Add(splitContainer1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Form1";
            Text = "PS2 DATA File Extractor";
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TreeView treeView1;
        private RichTextBox richTextBox1;
        private PictureBox pictureBox1;
        private SplitContainer splitContainer1;
        private TextEditorControl textEditorControl1;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem openmetFileToolStripMenuItem;
        private ToolStripMenuItem saveFileToPCToolStripMenuItem;
        private TableLayoutPanel tableLayoutPanel1;
    }
}
