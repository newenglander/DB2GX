﻿using System;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;
using Npgsql;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows;

namespace PG2GX
{
    

    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("odbccp32", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SQLConfigDataSourceW(UInt32 hwndParent, RequestFlags fRequest, string lpszDriver, string lpszAttributes);
        //static extern bool SQLWriteDSNToIni(string lpszDSN, string lpszDriver);
        //public static extern SQL_RETURN_CODE SQLInstallerError(int iError, ref int pfErrorCode, StringBuilder lpszErrorMsg, int cbErrorMsgMax, ref int pcbErrorMsg);

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

        private void databases_Loaded(object sender, RoutedEventArgs e)
        {

            
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (this.databases.SelectedIndex == -1)
            {
                MessageBox.Show("nichts ausgewählt!");
                return;
            }

            // create odbc connection
            
            try
            {
                String dbName = databases.SelectedValue.ToString();
                String dbServerName = databaseServers.SelectedValue.ToString();
                ODBCManager.CreateDSN(dbName , dbServerName, "PostgreSQL ANSI", true, dbName);
                RegistryManager.CreateEntry("HISMBS-GX", dbName, dbServerName);
            }
            catch (Exception ex)
            {
                TextBlockStatus.Text = ex.Message;
                return;
            }
            TextBlockStatus.Text = "Erfolg";
            
            

            // create registry entries
        }

        private void databaseServers_Loaded(object sender, RoutedEventArgs e)
        {
            databaseServers.Items.Add("localhost");
            databaseServers.Items.Add("castor");
            databaseServers.Items.Add("vmpostgres90");

        }

        private void databaseServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            
            String conn = "Server=" + databaseServers.SelectedItem.ToString() + ";Port=5432;Integrated Security=true;User Id=fsv;Password=fsv.fsv;Database=postgres;";
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

            foreach (String blah1 in sortedDBs)
            {
                this.databases.Items.Add(blah1.ToString());
            }
        }
        

    }


    public static class RegistryManager
    {
        private const string HIS_REG_PATH = "SOFTWARE\\HIS\\";
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
            var driversKey = Registry.LocalMachine.CreateSubKey(ODBCINST_INI_REG_PATH + "ODBC Drivers");
            if (driversKey == null) throw new Exception("ODBC Registry key for drivers does not exist");

            return driversKey.GetValue(dsnName) != null;
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
