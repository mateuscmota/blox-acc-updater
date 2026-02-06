namespace RBX_Alt_Manager.Forms
{
    partial class ThemeEditor
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SelectColor = new System.Windows.Forms.ColorDialog();
            this.pnlPresets = new System.Windows.Forms.Panel();
            this.btnPresetEscuro = new System.Windows.Forms.Button();
            this.btnPresetClaro = new System.Windows.Forms.Button();
            this.btnPresetAzul = new System.Windows.Forms.Button();
            this.btnPresetRoxo = new System.Windows.Forms.Button();
            this.btnPresetVerde = new System.Windows.Forms.Button();
            this.btnRestaurar = new System.Windows.Forms.Button();
            this.pnlBottom = new System.Windows.Forms.Panel();
            this.grpPreview = new System.Windows.Forms.GroupBox();
            this.btnSalvar = new System.Windows.Forms.Button();
            this.btnFechar = new System.Windows.Forms.Button();
            this.pnlMain = new System.Windows.Forms.Panel();
            this.pnlPresets.SuspendLayout();
            this.pnlBottom.SuspendLayout();
            this.SuspendLayout();
            //
            // pnlPresets
            //
            this.pnlPresets.Controls.Add(this.btnPresetEscuro);
            this.pnlPresets.Controls.Add(this.btnPresetClaro);
            this.pnlPresets.Controls.Add(this.btnPresetAzul);
            this.pnlPresets.Controls.Add(this.btnPresetRoxo);
            this.pnlPresets.Controls.Add(this.btnPresetVerde);
            this.pnlPresets.Controls.Add(this.btnRestaurar);
            this.pnlPresets.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlPresets.Location = new System.Drawing.Point(0, 0);
            this.pnlPresets.Name = "pnlPresets";
            this.pnlPresets.Size = new System.Drawing.Size(580, 45);
            this.pnlPresets.TabIndex = 0;
            //
            // btnPresetEscuro
            //
            this.btnPresetEscuro.Location = new System.Drawing.Point(8, 10);
            this.btnPresetEscuro.Name = "btnPresetEscuro";
            this.btnPresetEscuro.Size = new System.Drawing.Size(72, 26);
            this.btnPresetEscuro.TabIndex = 0;
            this.btnPresetEscuro.Text = "Escuro";
            this.btnPresetEscuro.UseVisualStyleBackColor = true;
            this.btnPresetEscuro.Click += new System.EventHandler(this.btnPresetEscuro_Click);
            //
            // btnPresetClaro
            //
            this.btnPresetClaro.Location = new System.Drawing.Point(84, 10);
            this.btnPresetClaro.Name = "btnPresetClaro";
            this.btnPresetClaro.Size = new System.Drawing.Size(72, 26);
            this.btnPresetClaro.TabIndex = 1;
            this.btnPresetClaro.Text = "Claro";
            this.btnPresetClaro.UseVisualStyleBackColor = true;
            this.btnPresetClaro.Click += new System.EventHandler(this.btnPresetClaro_Click);
            //
            // btnPresetAzul
            //
            this.btnPresetAzul.Location = new System.Drawing.Point(160, 10);
            this.btnPresetAzul.Name = "btnPresetAzul";
            this.btnPresetAzul.Size = new System.Drawing.Size(72, 26);
            this.btnPresetAzul.TabIndex = 2;
            this.btnPresetAzul.Text = "Azul";
            this.btnPresetAzul.UseVisualStyleBackColor = true;
            this.btnPresetAzul.Click += new System.EventHandler(this.btnPresetAzul_Click);
            //
            // btnPresetRoxo
            //
            this.btnPresetRoxo.Location = new System.Drawing.Point(236, 10);
            this.btnPresetRoxo.Name = "btnPresetRoxo";
            this.btnPresetRoxo.Size = new System.Drawing.Size(72, 26);
            this.btnPresetRoxo.TabIndex = 3;
            this.btnPresetRoxo.Text = "Roxo";
            this.btnPresetRoxo.UseVisualStyleBackColor = true;
            this.btnPresetRoxo.Click += new System.EventHandler(this.btnPresetRoxo_Click);
            //
            // btnPresetVerde
            //
            this.btnPresetVerde.Location = new System.Drawing.Point(312, 10);
            this.btnPresetVerde.Name = "btnPresetVerde";
            this.btnPresetVerde.Size = new System.Drawing.Size(72, 26);
            this.btnPresetVerde.TabIndex = 4;
            this.btnPresetVerde.Text = "Verde";
            this.btnPresetVerde.UseVisualStyleBackColor = true;
            this.btnPresetVerde.Click += new System.EventHandler(this.btnPresetVerde_Click);
            //
            // btnRestaurar
            //
            this.btnRestaurar.Location = new System.Drawing.Point(400, 10);
            this.btnRestaurar.Name = "btnRestaurar";
            this.btnRestaurar.Size = new System.Drawing.Size(130, 26);
            this.btnRestaurar.TabIndex = 5;
            this.btnRestaurar.Text = "Restaurar Original";
            this.btnRestaurar.UseVisualStyleBackColor = true;
            this.btnRestaurar.Click += new System.EventHandler(this.btnRestaurar_Click);
            //
            // pnlBottom
            //
            this.pnlBottom.Controls.Add(this.grpPreview);
            this.pnlBottom.Controls.Add(this.btnSalvar);
            this.pnlBottom.Controls.Add(this.btnFechar);
            this.pnlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlBottom.Location = new System.Drawing.Point(0, 410);
            this.pnlBottom.Name = "pnlBottom";
            this.pnlBottom.Size = new System.Drawing.Size(580, 140);
            this.pnlBottom.TabIndex = 1;
            //
            // grpPreview
            //
            this.grpPreview.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpPreview.Location = new System.Drawing.Point(10, 5);
            this.grpPreview.Name = "grpPreview";
            this.grpPreview.Size = new System.Drawing.Size(560, 85);
            this.grpPreview.TabIndex = 0;
            this.grpPreview.TabStop = false;
            this.grpPreview.Text = "Preview";
            //
            // btnSalvar
            //
            this.btnSalvar.Location = new System.Drawing.Point(160, 100);
            this.btnSalvar.Name = "btnSalvar";
            this.btnSalvar.Size = new System.Drawing.Size(130, 30);
            this.btnSalvar.TabIndex = 1;
            this.btnSalvar.Text = "Salvar e Aplicar";
            this.btnSalvar.UseVisualStyleBackColor = true;
            this.btnSalvar.Click += new System.EventHandler(this.btnSalvar_Click);
            //
            // btnFechar
            //
            this.btnFechar.Location = new System.Drawing.Point(300, 100);
            this.btnFechar.Name = "btnFechar";
            this.btnFechar.Size = new System.Drawing.Size(130, 30);
            this.btnFechar.TabIndex = 2;
            this.btnFechar.Text = "Fechar";
            this.btnFechar.UseVisualStyleBackColor = true;
            this.btnFechar.Click += new System.EventHandler(this.btnFechar_Click);
            //
            // pnlMain
            //
            this.pnlMain.AutoScroll = true;
            this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMain.Location = new System.Drawing.Point(0, 45);
            this.pnlMain.Name = "pnlMain";
            this.pnlMain.Size = new System.Drawing.Size(580, 365);
            this.pnlMain.TabIndex = 2;
            //
            // ThemeEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(580, 550);
            this.Controls.Add(this.pnlMain);
            this.Controls.Add(this.pnlBottom);
            this.Controls.Add(this.pnlPresets);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(580, 400);
            this.Name = "ThemeEditor";
            this.ShowIcon = false;
            this.Text = "Editor de Tema";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ThemeEditor_FormClosing);
            this.pnlPresets.ResumeLayout(false);
            this.pnlBottom.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.ColorDialog SelectColor;
        private System.Windows.Forms.Panel pnlPresets;
        private System.Windows.Forms.Button btnPresetEscuro;
        private System.Windows.Forms.Button btnPresetClaro;
        private System.Windows.Forms.Button btnPresetAzul;
        private System.Windows.Forms.Button btnPresetRoxo;
        private System.Windows.Forms.Button btnPresetVerde;
        private System.Windows.Forms.Button btnRestaurar;
        private System.Windows.Forms.Panel pnlBottom;
        private System.Windows.Forms.GroupBox grpPreview;
        private System.Windows.Forms.Button btnSalvar;
        private System.Windows.Forms.Button btnFechar;
        private System.Windows.Forms.Panel pnlMain;
    }
}
