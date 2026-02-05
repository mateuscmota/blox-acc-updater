using System;
using System.Windows.Forms;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Controls
{
    public partial class PrivateServerItem : UserControl
    {
        public PrivateServer Server { get; private set; }
        
        // Eventos para o form principal tratar
        public event EventHandler JoinClicked;
        public event EventHandler CopyClicked;
        public event EventHandler EditClicked;
        public event EventHandler DeleteClicked;

        public PrivateServerItem()
        {
            InitializeComponent();
        }

        public PrivateServerItem(PrivateServer server) : this()
        {
            SetServer(server);
        }

        public void SetServer(PrivateServer server)
        {
            Server = server;
            NameLabel.Text = server.Name;
            
            // Truncar link se muito longo
            string displayLink = server.Link.Length > 25 
                ? server.Link.Substring(0, 25) + "..." 
                : server.Link;
            LinkLabel.Text = displayLink;
        }

        private void JoinButton_Click(object sender, EventArgs e)
        {
            JoinClicked?.Invoke(this, EventArgs.Empty);
        }

        private void CopyButton_Click(object sender, EventArgs e)
        {
            if (Server != null)
            {
                Clipboard.SetText(Server.Link);
            }
            CopyClicked?.Invoke(this, EventArgs.Empty);
        }

        private void EditButton_Click(object sender, EventArgs e)
        {
            EditClicked?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            DeleteClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
