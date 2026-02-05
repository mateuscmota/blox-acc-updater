namespace RBX_Alt_Manager.Controls
{
    partial class EstoquePanelControl
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
            this.EstoqueTitleLabel = new System.Windows.Forms.Label();
            this.EstoqueUserLabel = new System.Windows.Forms.Label();
            this.EstoqueGameLabel = new System.Windows.Forms.Label();
            this.EstoqueItemsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.ExampleItemPanel1 = new System.Windows.Forms.Panel();
            this.ExampleItemName1 = new System.Windows.Forms.Label();
            this.ExampleMinusBtn1 = new System.Windows.Forms.Button();
            this.ExampleQtyTextBox1 = new System.Windows.Forms.TextBox();
            this.ExamplePlusBtn1 = new System.Windows.Forms.Button();
            this.EstoqueRefreshButton = new System.Windows.Forms.Button();
            this.EstoqueItemsPanel.SuspendLayout();
            this.ExampleItemPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // EstoqueTitleLabel
            // 
            this.EstoqueTitleLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.EstoqueTitleLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.EstoqueTitleLabel.ForeColor = System.Drawing.Color.White;
            this.EstoqueTitleLabel.Location = new System.Drawing.Point(0, 0);
            this.EstoqueTitleLabel.Name = "EstoqueTitleLabel";
            this.EstoqueTitleLabel.Size = new System.Drawing.Size(160, 22);
            this.EstoqueTitleLabel.TabIndex = 0;
            this.EstoqueTitleLabel.Text = "ALTERAR ESTOQUE";
            this.EstoqueTitleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // EstoqueUserLabel
            // 
            this.EstoqueUserLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.EstoqueUserLabel.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.EstoqueUserLabel.ForeColor = System.Drawing.Color.Cyan;
            this.EstoqueUserLabel.Location = new System.Drawing.Point(0, 22);
            this.EstoqueUserLabel.Name = "EstoqueUserLabel";
            this.EstoqueUserLabel.Size = new System.Drawing.Size(160, 18);
            this.EstoqueUserLabel.TabIndex = 1;
            this.EstoqueUserLabel.Text = "nomedaconta";
            this.EstoqueUserLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // EstoqueGameLabel
            // 
            this.EstoqueGameLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.EstoqueGameLabel.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.EstoqueGameLabel.ForeColor = System.Drawing.Color.LimeGreen;
            this.EstoqueGameLabel.Location = new System.Drawing.Point(0, 40);
            this.EstoqueGameLabel.Name = "EstoqueGameLabel";
            this.EstoqueGameLabel.Size = new System.Drawing.Size(160, 18);
            this.EstoqueGameLabel.TabIndex = 2;
            this.EstoqueGameLabel.Text = "BLOX FRUITS";
            this.EstoqueGameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // EstoqueItemsPanel
            // 
            this.EstoqueItemsPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EstoqueItemsPanel.AutoScroll = true;
            this.EstoqueItemsPanel.Controls.Add(this.ExampleItemPanel1);
            this.EstoqueItemsPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.EstoqueItemsPanel.Location = new System.Drawing.Point(3, 61);
            this.EstoqueItemsPanel.Name = "EstoqueItemsPanel";
            this.EstoqueItemsPanel.Size = new System.Drawing.Size(154, 506);
            this.EstoqueItemsPanel.TabIndex = 3;
            this.EstoqueItemsPanel.WrapContents = false;
            // 
            // ExampleItemPanel1
            // 
            this.ExampleItemPanel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.ExampleItemPanel1.Controls.Add(this.ExampleItemName1);
            this.ExampleItemPanel1.Controls.Add(this.ExampleMinusBtn1);
            this.ExampleItemPanel1.Controls.Add(this.ExampleQtyTextBox1);
            this.ExampleItemPanel1.Controls.Add(this.ExamplePlusBtn1);
            this.ExampleItemPanel1.Location = new System.Drawing.Point(0, 1);
            this.ExampleItemPanel1.Margin = new System.Windows.Forms.Padding(0, 1, 0, 1);
            this.ExampleItemPanel1.Name = "ExampleItemPanel1";
            this.ExampleItemPanel1.Size = new System.Drawing.Size(154, 38);
            this.ExampleItemPanel1.TabIndex = 0;
            // 
            // ExampleItemName1
            // 
            this.ExampleItemName1.AutoEllipsis = true;
            this.ExampleItemName1.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.ExampleItemName1.ForeColor = System.Drawing.Color.LightGray;
            this.ExampleItemName1.Location = new System.Drawing.Point(2, 1);
            this.ExampleItemName1.Name = "ExampleItemName1";
            this.ExampleItemName1.Size = new System.Drawing.Size(140, 15);
            this.ExampleItemName1.TabIndex = 0;
            this.ExampleItemName1.Text = "BRAINROT 100M/S";
            // 
            // ExampleMinusBtn1
            // 
            this.ExampleMinusBtn1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(80)))), ((int)(((byte)(80)))));
            this.ExampleMinusBtn1.FlatAppearance.BorderSize = 0;
            this.ExampleMinusBtn1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExampleMinusBtn1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.ExampleMinusBtn1.ForeColor = System.Drawing.Color.White;
            this.ExampleMinusBtn1.Location = new System.Drawing.Point(7, 17);
            this.ExampleMinusBtn1.Name = "ExampleMinusBtn1";
            this.ExampleMinusBtn1.Size = new System.Drawing.Size(30, 15);
            this.ExampleMinusBtn1.TabIndex = 1;
            this.ExampleMinusBtn1.Text = "-";
            this.ExampleMinusBtn1.UseVisualStyleBackColor = false;
            // 
            // ExampleQtyTextBox1
            // 
            this.ExampleQtyTextBox1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ExampleQtyTextBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.ExampleQtyTextBox1.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.ExampleQtyTextBox1.ForeColor = System.Drawing.Color.White;
            this.ExampleQtyTextBox1.Location = new System.Drawing.Point(42, 17);
            this.ExampleQtyTextBox1.Name = "ExampleQtyTextBox1";
            this.ExampleQtyTextBox1.Size = new System.Drawing.Size(70, 15);
            this.ExampleQtyTextBox1.TabIndex = 2;
            this.ExampleQtyTextBox1.Text = "10";
            this.ExampleQtyTextBox1.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // ExamplePlusBtn1
            // 
            this.ExamplePlusBtn1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(0)))));
            this.ExamplePlusBtn1.FlatAppearance.BorderSize = 0;
            this.ExamplePlusBtn1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ExamplePlusBtn1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.ExamplePlusBtn1.ForeColor = System.Drawing.Color.White;
            this.ExamplePlusBtn1.Location = new System.Drawing.Point(117, 17);
            this.ExamplePlusBtn1.Name = "ExamplePlusBtn1";
            this.ExamplePlusBtn1.Size = new System.Drawing.Size(30, 15);
            this.ExamplePlusBtn1.TabIndex = 3;
            this.ExamplePlusBtn1.Text = "+";
            this.ExamplePlusBtn1.UseVisualStyleBackColor = false;
            // 
            // EstoqueRefreshButton
            // 
            this.EstoqueRefreshButton.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EstoqueRefreshButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.EstoqueRefreshButton.ForeColor = System.Drawing.Color.White;
            this.EstoqueRefreshButton.Location = new System.Drawing.Point(3, 570);
            this.EstoqueRefreshButton.Name = "EstoqueRefreshButton";
            this.EstoqueRefreshButton.Size = new System.Drawing.Size(154, 25);
            this.EstoqueRefreshButton.TabIndex = 4;
            this.EstoqueRefreshButton.Text = "🔄 Atualizar";
            this.EstoqueRefreshButton.UseVisualStyleBackColor = true;
            this.EstoqueRefreshButton.Click += new System.EventHandler(this.RefreshButton_Click);
            // 
            // EstoquePanelControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.EstoqueRefreshButton);
            this.Controls.Add(this.EstoqueItemsPanel);
            this.Controls.Add(this.EstoqueGameLabel);
            this.Controls.Add(this.EstoqueUserLabel);
            this.Controls.Add(this.EstoqueTitleLabel);
            this.Name = "EstoquePanelControl";
            this.Size = new System.Drawing.Size(160, 600);
            this.EstoqueItemsPanel.ResumeLayout(false);
            this.ExampleItemPanel1.ResumeLayout(false);
            this.ExampleItemPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label EstoqueTitleLabel;
        private System.Windows.Forms.Label EstoqueUserLabel;
        private System.Windows.Forms.Label EstoqueGameLabel;
        private System.Windows.Forms.FlowLayoutPanel EstoqueItemsPanel;
        private System.Windows.Forms.Button EstoqueRefreshButton;
        private System.Windows.Forms.Panel ExampleItemPanel1;
        private System.Windows.Forms.Label ExampleItemName1;
        private System.Windows.Forms.Button ExampleMinusBtn1;
        private System.Windows.Forms.TextBox ExampleQtyTextBox1;
        private System.Windows.Forms.Button ExamplePlusBtn1;
    }
}
