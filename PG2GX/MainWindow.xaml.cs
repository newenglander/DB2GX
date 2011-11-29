using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Data;
using System.Data.OleDb;

using Npgsql;

namespace PG2GX
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void comboBox1_Loaded(object sender, RoutedEventArgs e)
        {

            String conn = "Server=127.0.0.1;Port=5432;Integrated Security=true;User Id=postgres;Password=postgres";




            NpgsqlConnection sqlConx;
            try
            {
                sqlConx = new NpgsqlConnection(conn);
                sqlConx.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
                return;
            }
                
            DataTable tblDatabases = sqlConx.GetSchema("Databases");
            sqlConx.Close();

            foreach (DataRow row in tblDatabases.Rows)
            {
                //Console.WriteLine("Database: " + row["database_name"]);
                this.comboBox1.Items.Add(row["database_name"]);
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (this.comboBox1.SelectedIndex == -1)
            {
                MessageBox.Show("nichts ausgewählt!");
                return;
            }
        }
    }
}
