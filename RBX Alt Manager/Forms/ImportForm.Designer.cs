namespace RBX_Alt_Manager
{
    partial class ImportForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.Accounts = new System.Windows.Forms.RichTextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.ImportButton = new System.Windows.Forms.Button();
            this.BypassCheckBox = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // Accounts
            // 
            this.Accounts.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Accounts.Location = new System.Drawing.Point(12, 25);
            this.Accounts.Name = "Accounts";
            this.Accounts.Size = new System.Drawing.Size(573, 137);
            this.Accounts.TabIndex = 0;
            this.Accounts.Text = "";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(306, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Cole os cookies aqui (um por linha)";
            // 
            // ImportButton
            // 
            this.ImportButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ImportButton.Location = new System.Drawing.Point(12, 168);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(85, 25);
            this.ImportButton.TabIndex = 2;
            this.ImportButton.Text = "Importar";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // BypassCheckBox
            // 
            this.BypassCheckBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.BypassCheckBox.AutoSize = true;
            this.BypassCheckBox.Location = new System.Drawing.Point(103, 172);
            this.BypassCheckBox.Name = "BypassCheckBox";
            this.BypassCheckBox.Size = new System.Drawing.Size(145, 17);
            this.BypassCheckBox.TabIndex = 3;
            this.BypassCheckBox.Text = "🔄 Bypass Cookie (Auto)";
            this.BypassCheckBox.UseVisualStyleBackColor = true;
            // 
            // ImportForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(597, 205);
            this.Controls.Add(this.BypassCheckBox);
            this.Controls.Add(this.ImportButton);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Accounts);
            this.Name = "ImportForm";
            this.ShowIcon = false;
            this.Text = "Importar Cookies";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ImportForm_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RichTextBox Accounts;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button ImportButton;
        private System.Windows.Forms.CheckBox BypassCheckBox;
    }
}