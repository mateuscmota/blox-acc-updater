namespace RBX_Alt_Manager.Controls
{
    partial class GameSelectorPanelControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.GameSelectorTitleLabel = new System.Windows.Forms.Label();
            this.GameSelectorSearchTextBox = new System.Windows.Forms.TextBox();
            this.GameSelectorButtonsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.ExampleGameButton1 = new System.Windows.Forms.Button();
            this.ExampleGameButton2 = new System.Windows.Forms.Button();
            this.ExampleGameButton3 = new System.Windows.Forms.Button();
            this.ExampleGameButton4 = new System.Windows.Forms.Button();
            this.ExampleGameButton5 = new System.Windows.Forms.Button();
            this.GameSelectorBackButton = new System.Windows.Forms.Button();
            this.GameSelectorButtonsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // GameSelectorTitleLabel
            // 
            this.GameSelectorTitleLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.GameSelectorTitleLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.GameSelectorTitleLabel.ForeColor = System.Drawing.Color.White;
            this.GameSelectorTitleLabel.Location = new System.Drawing.Point(0, 0);
            this.GameSelectorTitleLabel.Name = "GameSelectorTitleLabel";
            this.GameSelectorTitleLabel.Size = new System.Drawing.Size(265, 28);
            this.GameSelectorTitleLabel.TabIndex = 0;
            this.GameSelectorTitleLabel.Text = "JOGOS";
            this.GameSelectorTitleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // GameSelectorSearchTextBox
            // 
            this.GameSelectorSearchTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.GameSelectorSearchTextBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.GameSelectorSearchTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.GameSelectorSearchTextBox.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.GameSelectorSearchTextBox.ForeColor = System.Drawing.Color.White;
            this.GameSelectorSearchTextBox.Location = new System.Drawing.Point(3, 31);
            this.GameSelectorSearchTextBox.Name = "GameSelectorSearchTextBox";
            this.GameSelectorSearchTextBox.Size = new System.Drawing.Size(259, 22);
            this.GameSelectorSearchTextBox.TabIndex = 1;
            this.GameSelectorSearchTextBox.Text = "🔍 Buscar...";
            this.GameSelectorSearchTextBox.TextChanged += new System.EventHandler(this.SearchTextBox_TextChanged);
            this.GameSelectorSearchTextBox.Enter += new System.EventHandler(this.SearchTextBox_Enter);
            this.GameSelectorSearchTextBox.Leave += new System.EventHandler(this.SearchTextBox_Leave);
            // 
            // GameSelectorButtonsPanel
            // 
            this.GameSelectorButtonsPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.GameSelectorButtonsPanel.AutoScroll = true;
            this.GameSelectorButtonsPanel.Controls.Add(this.ExampleGameButton1);
            this.GameSelectorButtonsPanel.Controls.Add(this.ExampleGameButton2);
            this.GameSelectorButtonsPanel.Controls.Add(this.ExampleGameButton3);
            this.GameSelectorButtonsPanel.Controls.Add(this.ExampleGameButton4);
            this.GameSelectorButtonsPanel.Controls.Add(this.ExampleGameButton5);
            this.GameSelectorButtonsPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.GameSelectorButtonsPanel.Location = new System.Drawing.Point(3, 56);
            this.GameSelectorButtonsPanel.Name = "GameSelectorButtonsPanel";
            this.GameSelectorButtonsPanel.Size = new System.Drawing.Size(259, 508);
            this.GameSelectorButtonsPanel.TabIndex = 2;
            this.GameSelectorButtonsPanel.WrapContents = false;
            // 
            // ExampleGameButton1
            // 
            this.ExampleGameButton1.BackColor = System.Drawing.Color.White;
            this.ExampleGameButton1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExampleGameButton1.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ExampleGameButton1.ForeColor = System.Drawing.Color.Black;
            this.ExampleGameButton1.Location = new System.Drawing.Point(3, 3);
            this.ExampleGameButton1.Name = "ExampleGameButton1";
            this.ExampleGameButton1.Size = new System.Drawing.Size(256, 35);
            this.ExampleGameButton1.TabIndex = 0;
            this.ExampleGameButton1.Text = "blox fruits";
            this.ExampleGameButton1.UseVisualStyleBackColor = false;
            // 
            // ExampleGameButton2
            // 
            this.ExampleGameButton2.BackColor = System.Drawing.Color.White;
            this.ExampleGameButton2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExampleGameButton2.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ExampleGameButton2.ForeColor = System.Drawing.Color.Black;
            this.ExampleGameButton2.Location = new System.Drawing.Point(3, 44);
            this.ExampleGameButton2.Name = "ExampleGameButton2";
            this.ExampleGameButton2.Size = new System.Drawing.Size(256, 35);
            this.ExampleGameButton2.TabIndex = 1;
            this.ExampleGameButton2.Text = "steal a brainrot";
            this.ExampleGameButton2.UseVisualStyleBackColor = false;
            // 
            // ExampleGameButton3
            // 
            this.ExampleGameButton3.BackColor = System.Drawing.Color.White;
            this.ExampleGameButton3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExampleGameButton3.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ExampleGameButton3.ForeColor = System.Drawing.Color.Black;
            this.ExampleGameButton3.Location = new System.Drawing.Point(3, 85);
            this.ExampleGameButton3.Name = "ExampleGameButton3";
            this.ExampleGameButton3.Size = new System.Drawing.Size(256, 35);
            this.ExampleGameButton3.TabIndex = 2;
            this.ExampleGameButton3.Text = "murder mystery 2";
            this.ExampleGameButton3.UseVisualStyleBackColor = false;
            // 
            // ExampleGameButton4
            // 
            this.ExampleGameButton4.BackColor = System.Drawing.Color.White;
            this.ExampleGameButton4.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExampleGameButton4.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ExampleGameButton4.ForeColor = System.Drawing.Color.Black;
            this.ExampleGameButton4.Location = new System.Drawing.Point(3, 126);
            this.ExampleGameButton4.Name = "ExampleGameButton4";
            this.ExampleGameButton4.Size = new System.Drawing.Size(256, 35);
            this.ExampleGameButton4.TabIndex = 3;
            this.ExampleGameButton4.Text = "grow a garden";
            this.ExampleGameButton4.UseVisualStyleBackColor = false;
            // 
            // ExampleGameButton5
            // 
            this.ExampleGameButton5.BackColor = System.Drawing.Color.White;
            this.ExampleGameButton5.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExampleGameButton5.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ExampleGameButton5.ForeColor = System.Drawing.Color.Black;
            this.ExampleGameButton5.Location = new System.Drawing.Point(3, 167);
            this.ExampleGameButton5.Name = "ExampleGameButton5";
            this.ExampleGameButton5.Size = new System.Drawing.Size(256, 35);
            this.ExampleGameButton5.TabIndex = 4;
            this.ExampleGameButton5.Text = "escape tsunami";
            this.ExampleGameButton5.UseVisualStyleBackColor = false;
            // 
            // GameSelectorBackButton
            // 
            this.GameSelectorBackButton.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.GameSelectorBackButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.GameSelectorBackButton.ForeColor = System.Drawing.Color.White;
            this.GameSelectorBackButton.Location = new System.Drawing.Point(3, 570);
            this.GameSelectorBackButton.Name = "GameSelectorBackButton";
            this.GameSelectorBackButton.Size = new System.Drawing.Size(259, 25);
            this.GameSelectorBackButton.TabIndex = 3;
            this.GameSelectorBackButton.Text = "VOLTAR";
            this.GameSelectorBackButton.UseVisualStyleBackColor = true;
            this.GameSelectorBackButton.Visible = false;
            this.GameSelectorBackButton.Click += new System.EventHandler(this.BackButton_Click);
            // 
            // GameSelectorPanelControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(40)))), ((int)(((byte)(40)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.GameSelectorBackButton);
            this.Controls.Add(this.GameSelectorButtonsPanel);
            this.Controls.Add(this.GameSelectorSearchTextBox);
            this.Controls.Add(this.GameSelectorTitleLabel);
            this.Name = "GameSelectorPanelControl";
            this.Size = new System.Drawing.Size(265, 600);
            this.Load += new System.EventHandler(this.GameSelectorPanelControl_Load);
            this.GameSelectorButtonsPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label GameSelectorTitleLabel;
        private System.Windows.Forms.TextBox GameSelectorSearchTextBox;
        private System.Windows.Forms.FlowLayoutPanel GameSelectorButtonsPanel;
        private System.Windows.Forms.Button GameSelectorBackButton;
        private System.Windows.Forms.Button ExampleGameButton1;
        private System.Windows.Forms.Button ExampleGameButton2;
        private System.Windows.Forms.Button ExampleGameButton3;
        private System.Windows.Forms.Button ExampleGameButton4;
        private System.Windows.Forms.Button ExampleGameButton5;
    }
}
