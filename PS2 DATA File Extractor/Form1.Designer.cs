﻿namespace PS2_DATA_File_Extractor
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
            treeView1 = new TreeView();
            button1 = new Button();
            richTextBox1 = new RichTextBox();
            backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            SuspendLayout();
            // 
            // treeView1
            // 
            treeView1.Location = new Point(12, 12);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(229, 683);
            treeView1.TabIndex = 0;
            // 
            // button1
            // 
            button1.Location = new Point(392, 602);
            button1.Name = "button1";
            button1.Size = new Size(156, 47);
            button1.TabIndex = 1;
            button1.Text = "Open data.met file";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // richTextBox1
            // 
            richTextBox1.Location = new Point(282, 26);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new Size(855, 515);
            richTextBox1.TabIndex = 2;
            richTextBox1.Text = "";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1223, 736);
            Controls.Add(richTextBox1);
            Controls.Add(button1);
            Controls.Add(treeView1);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private TreeView treeView1;
        private Button button1;
        private RichTextBox richTextBox1;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
    }
}