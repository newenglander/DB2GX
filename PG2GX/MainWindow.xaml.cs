using System;
using System.Windows.Controls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Npgsql;
using System.DirectoryServices.ActiveDirectory;
using System.Web;
using System.Net.Sockets;

namespace PG2GX
{
    

    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {        
        ArrayList localhostDBs;
        ArrayList castorDBs;
        ArrayList vmpostgres90DBs;

        public enum SQL_RETURN_CODE : int
        {
            SQL_ERROR = -1,
            SQL_INVALID_HANDLE = -2,
            SQL_SUCCESS = 0,
            SQL_SUCCESS_WITH_INFO = 1,
            SQL_STILL_EXECUTING = 2,
            SQL_NEED_DATA = 99,
            SQL_NO_DATA = 100
        }

        enum RequestFlags : int
        {
            ODBC_ADD_DSN = 1,
            ODBC_CONFIG_DSN = 2,
            ODBC_REMOVE_DSN = 3,
            ODBC_ADD_SYS_DSN = 4,
            ODBC_CONFIG_SYS_DSN = 5,
            ODBC_REMOVE_SYS_DSN = 6,
            ODBC_REMOVE_DEFAULT_DSN = 7
        }


        public MainWindow()
        {
            InitializeComponent();
            localhostDBs = new ArrayList();
            castorDBs = new ArrayList();
            vmpostgres90DBs = new ArrayList();
        }        

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if ((this.databaseServers.SelectedIndex == -1) || (this.databases.SelectedIndex == -1) || (this.hisProduct.SelectedIndex == -1))
            {
                MessageBox.Show("nichts ausgewählt!");
                return;
            }
            
            try
            {
                String dbName = databases.SelectedValue.ToString();
                String dbServerName = databaseServers.SelectedValue.ToString();
                String hisProductName = ((ComboBoxItem)hisProduct.SelectedItem).Name;
                if (ODBCManager.DSNExists(dbName))
                {
                    MessageBoxResult res = MessageBox.Show("ODBC Eintrag " + dbName + " exisitiert schon! Fortfahren?", "Warnung", MessageBoxButton.YesNo);
                    if (res == MessageBoxResult.No) return;
                }

                if (RegistryManager.EntryExists(hisProductName, dbName, dbServerName))
                {
                    MessageBoxResult res = MessageBox.Show("Registry Eintrag " + dbName + " exisitiert schon! Fortfahren?", "Warnung", MessageBoxButton.YesNo);
                    if (res == MessageBoxResult.No) return;
                }
                // create odbc connection
                ODBCManager.CreateDSN(dbName, dbServerName, "PostgreSQL ANSI", true, dbName);
                // create registry entries
                RegistryManager.CreateEntry(hisProductName, dbName, dbServerName);
            }
            catch (Exception ex)
            {
                TextBlockStatus.Text = ex.Message;
                return;
            }
            TextBlockStatus.Text = "Erfolg";
        }

        private void databaseServers_Loaded(object sender, RoutedEventArgs e)
        {
            databaseServers.Items.Add(new MyComboBoxItem("localhost", "5432"));
            databaseServers.Items.Add(new MyComboBoxItem("castor1", "5432"));
            databaseServers.Items.Add(new MyComboBoxItem("castor2", "5431"));
            databaseServers.Items.Add(new MyComboBoxItem("vmpostgres90", "5432"));

            //ArrayList serverList = Win32.NetApi32.GetServerList(Win32.NetApi32.SV_101_TYPES.SV_TYPE_ALL);
            List<Win32.NetApi32.SERVER_INFO_101> serverList = Win32.NetApi32.GetServerList(Win32.NetApi32.SV_101_TYPES.SV_TYPE_ALL);            

            foreach (Win32.NetApi32.SERVER_INFO_101 server in serverList)
            {
                String nameToShow = server.sv101_comment;
                if (nameToShow.Contains("UB1"))
                {     
                    // this test takes too long
                    //if (openDBConnection(server.sv101_name, true) != null)                    
                    {
                        nameToShow = nameToShow.Replace("UB1", "");
                        nameToShow = nameToShow.Trim('-', ' ');
                        databaseServers.Items.Add(new MyComboBoxItem(nameToShow, server.sv101_name));
                    }
                }
            }            
        }

        private NpgsqlConnection openDBConnection(String host, String port, bool silent)
        {
            String conn = "Server=" + host + ";Port=" + port + ";Integrated Security=true;User Id=fsv;Password=fsv.fsv;Database=postgres;Timeout=1;CommandTimeout=1;";
            NpgsqlConnection sqlConx = null;
            try
            {
                sqlConx = new NpgsqlConnection(conn);
                sqlConx.Open();                
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show("Error: " + ex.Message);
                }
                return null;
            }
            return sqlConx;
        }

        private void databaseServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            NpgsqlConnection sqlConx = openDBConnection(((MyComboBoxItem)databaseServers.SelectedItem).Name.TrimEnd('1', '2'), ((MyComboBoxItem)databaseServers.SelectedItem).Value, false);

            if (sqlConx == null) return;

            DataTable tblDatabases = sqlConx.GetSchema("Databases");
                        
            sqlConx.Close();            

            this.databases.Items.Clear();

            ArrayList sortedDBs = new ArrayList();

            foreach (DataRow row in tblDatabases.Rows)
            {
                try
                {                    
                    String dbName = row["database_name"].ToString();
                    if ((dbName != "postgres") && (dbName != "template0") && (dbName != "template1"))
                    {
                        sortedDBs.Add(dbName);
                    }                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex.Message);
                    return;
                }
            }

            sortedDBs.Sort();

            foreach (String db in sortedDBs)
            {
                this.databases.Items.Add(db.ToString());
            }
        }

        private void hisProduct_Loaded(object sender, RoutedEventArgs e)
        {
            hisProduct.Items.Add("HISMBS-GX");
            hisProduct.Items.Add("HISFSV-GX");
        }     
    }

    public class MyComboBoxItem
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public MyComboBoxItem(string name, string value)
        {
            Name = name;
            Value = value;
        }
        public MyComboBoxItem(string name)
        {
            Name = name;
            Value = name;
        }
    }

    public static class RegistryManager
    {
        private const string HIS_REG_PATH = "SOFTWARE\\HIS\\";
        public static bool EntryExists(String productName, String databaseName, String databaseServer)
        {
            var dbKey = Registry.LocalMachine.OpenSubKey(HIS_REG_PATH + "\\" + productName + "\\" + "Datenbank\\" + databaseName);
            return dbKey != null;
        }

        public static void CreateEntry(String productName, String databaseName, String databaseServer)
        {
            var dbKey = Registry.LocalMachine.CreateSubKey(HIS_REG_PATH + "\\" + productName + "\\" + "Datenbank\\" + databaseName);
            if (dbKey == null) throw new Exception("Registry key for DB was not created");
            dbKey.SetValue("Name", databaseName);
            dbKey.SetValue("DB-Server", databaseServer);            
            dbKey.SetValue("Typ", 6, RegistryValueKind.DWord);
        }
    }

    ///<summary>
    /// Class to assist with creation and removal of ODBC DSN entries
    ///</summary>
    public static class ODBCManager
    {
        private const string ODBC_INI_REG_PATH = "SOFTWARE\\ODBC\\ODBC.INI\\";
        private const string ODBCINST_INI_REG_PATH = "SOFTWARE\\ODBC\\ODBCINST.INI\\";

        /// <summary>
        /// Creates a new DSN entry with the specified values. If the DSN exists, the values are updated.
        /// </summary>
        /// <param name="dsnName">Name of the DSN for use by client applications</param>
        /// <param name="server">Network name or IP address of database server</param>
        /// <param name="driverName">Name of the driver to use</param>
        /// <param name="trustedConnection">True to use NT authentication, false to require applications to supply username/password in the connection string</param>
        /// <param name="database">Name of the datbase to connect to</param>
        public static void CreateDSN(string dsnName, string server, string driverName, bool trustedConnection, string database)
        {
            // Lookup driver path from driver name
            var driverKey = Registry.LocalMachine.CreateSubKey(ODBCINST_INI_REG_PATH + driverName);
            if (driverKey == null) throw new Exception(string.Format("ODBC Registry key for driver '{0}' does not exist", driverName));
            string driverPath = driverKey.GetValue("Driver").ToString();

            // Add value to odbc data sources
            var datasourcesKey = Registry.LocalMachine.CreateSubKey(ODBC_INI_REG_PATH + "ODBC Data Sources");
            if (datasourcesKey == null) throw new Exception("ODBC Registry key for datasources does not exist");
            datasourcesKey.SetValue(dsnName, driverName);

            // Create new key in odbc.ini with dsn name and add values
            var dsnKey = Registry.LocalMachine.CreateSubKey(ODBC_INI_REG_PATH + dsnName);
            if (dsnKey == null) throw new Exception("ODBC Registry key for DSN was not created");
            dsnKey.SetValue("Database", database);            
            dsnKey.SetValue("Driver", driverPath);
            dsnKey.SetValue("LastUser", Environment.UserName);
            dsnKey.SetValue("Servername", server);
            
            dsnKey.SetValue("Database", database);
            dsnKey.SetValue("Trusted_Connection", trustedConnection ? "Yes" : "No");

            // HIS Extras
            dsnKey.SetValue("MaxLongVarcharSize", "32766");
            dsnKey.SetValue("MaxVarcharSize", "32766");
            dsnKey.SetValue("ConnSettings", "set+search%5fpath+TO+mbs");
            dsnKey.SetValue("Username", "fsv");
            dsnKey.SetValue("Password", "fsv.fsv");
            dsnKey.SetValue("Protocol", "7.4-2");
        }

        /// <summary>
        /// Removes a DSN entry
        /// </summary>
        /// <param name="dsnName">Name of the DSN to remove.</param>
        public static void RemoveDSN(string dsnName)
        {
            // Remove DSN key
            Registry.LocalMachine.DeleteSubKeyTree(ODBC_INI_REG_PATH + dsnName);

            // Remove DSN name from values list in ODBC Data Sources key
            var datasourcesKey = Registry.LocalMachine.CreateSubKey(ODBC_INI_REG_PATH + "ODBC Data Sources");
            if (datasourcesKey == null) throw new Exception("ODBC Registry key for datasources does not exist");
            datasourcesKey.DeleteValue(dsnName);
        }

        ///<summary>
        /// Checks the registry to see if a DSN exists with the specified name
        ///</summary>
        ///<param name="dsnName"></param>
        ///<returns></returns>
        public static bool DSNExists(string dsnName)
        {            
            var dsnKey = Registry.LocalMachine.OpenSubKey(ODBC_INI_REG_PATH + dsnName);

            return dsnKey != null;
        }

        ///<summary>
        /// Returns an array of driver names installed on the system
        ///</summary>
        ///<returns></returns>
        public static string[] GetInstalledDrivers()
        {
            var driversKey = Registry.LocalMachine.CreateSubKey(ODBCINST_INI_REG_PATH + "ODBC Drivers");
            if (driversKey == null) throw new Exception("ODBC Registry key for drivers does not exist");

            var driverNames = driversKey.GetValueNames();

            var ret = new List<string>();

            foreach (var driverName in driverNames)
            {
                if (driverName != "(Default)")
                {
                    ret.Add(driverName);
                }
            }

            return ret.ToArray();
        }
    }


}

namespace Win32
{
    /// <summary>
    /// Summary description for Class1.
    /// </summary>
    ///
    public class NetApi32
    {
        // constants
        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_MORE_DATA = 234;
        public enum SV_101_TYPES : uint
        {
            SV_TYPE_WORKSTATION = 0x00000001,
            SV_TYPE_SERVER = 0x00000002,
            SV_TYPE_SQLSERVER = 0x00000004,
            SV_TYPE_DOMAIN_CTRL = 0x00000008,
            SV_TYPE_DOMAIN_BAKCTRL = 0x00000010,
            SV_TYPE_TIME_SOURCE = 0x00000020,
            SV_TYPE_AFP = 0x00000040,
            SV_TYPE_NOVELL = 0x00000080,
            SV_TYPE_DOMAIN_MEMBER = 0x00000100,
            SV_TYPE_PRINTQ_SERVER = 0x00000200,
            SV_TYPE_DIALIN_SERVER = 0x00000400,
            SV_TYPE_XENIX_SERVER = 0x00000800,
            SV_TYPE_SERVER_UNIX = 0x00000800,
            SV_TYPE_NT = 0x00001000,
            SV_TYPE_WFW = 0x00002000,
            SV_TYPE_SERVER_MFPN = 0x00004000,
            SV_TYPE_SERVER_NT = 0x00008000,
            SV_TYPE_POTENTIAL_BROWSER = 0x00010000,
            SV_TYPE_BACKUP_BROWSER = 0x00020000,
            SV_TYPE_MASTER_BROWSER = 0x00040000,
            SV_TYPE_DOMAIN_MASTER = 0x00080000,
            SV_TYPE_SERVER_OSF = 0x00100000,
            SV_TYPE_SERVER_VMS = 0x00200000,
            SV_TYPE_WINDOWS = 0x00400000,
            SV_TYPE_DFS = 0x00800000,
            SV_TYPE_CLUSTER_NT = 0x01000000,
            SV_TYPE_TERMINALSERVER = 0x02000000,
            SV_TYPE_CLUSTER_VS_NT = 0x04000000,
            SV_TYPE_DCE = 0x10000000,
            SV_TYPE_ALTERNATE_XPORT = 0x20000000,
            SV_TYPE_LOCAL_LIST_ONLY = 0x40000000,
            SV_TYPE_DOMAIN_ENUM = 0x80000000,
            SV_TYPE_ALL = 0xFFFFFFFF
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVER_INFO_101
        {
            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.U4)]
            public UInt32 sv101_platform_id;
            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string sv101_name;

            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.U4)]
            public UInt32 sv101_version_major;
            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.U4)]
            public UInt32 sv101_version_minor;
            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.U4)]
            public UInt32 sv101_type;
            [MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string sv101_comment;
        };
        public enum PLATFORM_ID
        {
            PLATFORM_ID_DOS = 300,
            PLATFORM_ID_OS2 = 400,
            PLATFORM_ID_NT = 500,
            PLATFORM_ID_OSF = 600,
            PLATFORM_ID_VMS = 700
        }

        [DllImport("netapi32.dll", EntryPoint = "NetServerEnum")]
        public static extern int NetServerEnum([MarshalAs(UnmanagedType.LPWStr)]string servername,
           int level,
           out IntPtr bufptr,
           int prefmaxlen,
           ref int entriesread,
           ref int totalentries,
           SV_101_TYPES servertype,
           [MarshalAs(UnmanagedType.LPWStr)]string domain,
           IntPtr resume_handle);

        [DllImport("netapi32.dll", EntryPoint = "NetApiBufferFree")]
        public static extern int
            NetApiBufferFree(IntPtr buffer);

        [DllImport("Netapi32", CharSet = CharSet.Unicode)]
        private static extern int NetMessageBufferSend(
            string servername,
            string msgname,
            string fromname,
            string buf,
            int buflen);

        public static int NetMessageSend(string serverName, string messageName, string fromName, string strMsgBuffer, int iMsgBufferLen)
        {
            return NetMessageBufferSend(serverName, messageName, fromName, strMsgBuffer, iMsgBufferLen * 2);
        }

        

        //public static ArrayList GetServerList(NetApi32.SV_101_TYPES ServerType)
        public static List<SERVER_INFO_101> GetServerList(NetApi32.SV_101_TYPES ServerType)
        {
            int entriesread = 0, totalentries = 0;
            List<SERVER_INFO_101> alServers = new List<SERVER_INFO_101>();
            
            do
            {
                // Buffer to store the available servers
                // Filled by the NetServerEnum function
                IntPtr buf = new IntPtr();

                SERVER_INFO_101 server;
                int ret = NetServerEnum(null, 101, out buf, -1,
                    ref entriesread, ref totalentries,
                    ServerType, null, IntPtr.Zero);

                // if the function returned any data, fill the tree view
                if (ret == ERROR_SUCCESS ||
                    ret == ERROR_MORE_DATA ||
                    entriesread > 0)
                {
                    IntPtr ptr = buf;

                    for (int i = 0; i < entriesread; i++)
                    {
                        // cast pointer to a SERVER_INFO_101 structure
                        server = (SERVER_INFO_101)Marshal.PtrToStructure(ptr, typeof(SERVER_INFO_101));

                        //Cast the pointer to a ulong so this addition will work on 32-bit or 64-bit systems.
                        ptr = (IntPtr)((ulong)ptr + (ulong)Marshal.SizeOf(server));

                        // add the machine name and comment to the arrayList.
                        //You could return the entire structure here if desired
                        alServers.Add(server);
                    }
                }

                // free the buffer
                NetApiBufferFree(buf);

            }
            while
                (
                entriesread < totalentries &&
                entriesread != 0
                );

            try
            {
                
                alServers = alServers.OrderBy(item => item.sv101_comment).ToList<SERVER_INFO_101>();
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            return alServers;
        }
    }
}

