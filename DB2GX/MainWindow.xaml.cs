using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Npgsql;
using IBM.Data.Informix;
using System.Security.Principal;


namespace DB2GX
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ArrayList localhostDBs;
        ArrayList castorDBs;
        ArrayList vmpostgres90DBs;
        ArrayList userDBs; // not String, but special ComboBox!
        public BackgroundWorker bw;

        public const String HISMBSGX = "HISMBS-GX";
        public const String HISFSVGX = "HISFSV-GX";
        public const String HISSVAGX = "HISSVA-GX";
        public const String PGPORT = "5432";

        public const String PGANSI = "PostgreSQL ANSI";
        public const String PGUNICODE = "PostgreSQL Unicode";

        public const String DBPOSTGRES = "PostgreSQL";
        public const String DBINFORMIX = "Informix";

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

        private enum RequestFlags : int
        {
            ODBC_ADD_DSN = 1,
            ODBC_CONFIG_DSN = 2,
            ODBC_REMOVE_DSN = 3,
            ODBC_ADD_SYS_DSN = 4,
            ODBC_CONFIG_SYS_DSN = 5,
            ODBC_REMOVE_SYS_DSN = 6,
            ODBC_REMOVE_DEFAULT_DSN = 77
        }

        public MainWindow()
        {
            InitializeComponent();
            localhostDBs = new ArrayList();
            castorDBs = new ArrayList();
            vmpostgres90DBs = new ArrayList();
            userDBs = new ArrayList();

            if (!IsUserAdministrator())
            {
                MessageBox.Show("Kein Admin rechte!");
                return;
            }

            databaseServers.Focus();
        }

        // http://stackoverflow.com/a/1089061/381233
        public bool IsUserAdministrator()
        {
            //bool value to hold our return value
            bool isAdmin;
            try
            {
                //get the currently logged in user
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (UnauthorizedAccessException ex)
            {
                isAdmin = false;
            }
            catch (Exception ex)
            {
                isAdmin = false;
            }
            return isAdmin;
        }

        private String getHisProduct()
        {
            String hisProductName = (String)hisProduct.SelectedValue;

            return (hisProductName == null) ? hisProductName : hisProductName.Split(' ')[0];
        }


        private void createNewEntry()
        {
            if ((comboBoxDBType.SelectedIndex == -1) || (databaseServers.SelectedIndex == -1) || (databases.SelectedIndex == -1) || (hisProduct.SelectedIndex == -1) || 
                ((comboBoxEncoding.SelectedIndex == -1) && (comboBoxDBType.SelectedItem.ToString() == DBPOSTGRES)))
            {
                MessageBox.Show("Fehlende Eingabe!");
                return;
            }

            try
            {
                String setSearchPathTo = "";
                String dbName = databases.SelectedValue.ToString().Trim();
                String dbServerName = ((ComboBoxServer)(databaseServers.SelectedItem)).ServerName;
                String dbServerPort = ((ComboBoxServer)(databaseServers.SelectedItem)).Port;
                String hisProductName = getHisProduct();
                String entryName = dbServerName + "-" + dbName;

                if (entryName.Length > 30)
                {
                    entryName = dbServerName.Substring(0, 3) + "-" + dbName;
                }
                if (ODBCManager.DSNExists(entryName))
                {
                    MessageBoxResult res = MessageBox.Show("ODBC Eintrag " + entryName + " exisitiert schon! Fortfahren?", "Warnung", MessageBoxButton.YesNo);
                    if (res == MessageBoxResult.No) return;
                }

                if (RegistryManager.EntryExists(hisProductName, entryName, dbServerName))
                {
                    MessageBoxResult res = MessageBox.Show("Registry Eintrag " + dbName + " exisitiert schon! Fortfahren?", "Warnung", MessageBoxButton.YesNo);
                    if (res == MessageBoxResult.No) return;
                }

                if (comboBoxDBType.SelectedItem.ToString() == DBPOSTGRES)
                {

                    // create odbc connection
                    DBConnection pgConnection = new DBConnection(DBConnection.DBType.Postgres);
                    NpgsqlConnection con = (NpgsqlConnection)pgConnection.openPGConnection(dbServerName, dbName, dbServerPort, getHisProduct(), false);
                    // if a namespace 'mbs' exists, then we have a hisrm database and need to set the search path
                    String query = "SELECT COUNT(*) FROM pg_catalog.pg_namespace WHERE nspname IN ('mbs', 'sva4')";

                    NpgsqlCommand command = new NpgsqlCommand(query, con);
                    NpgsqlDataReader reader = command.ExecuteReader();
                    reader.Read();
                    if (int.Parse(reader[0].ToString()) > 0)
                    {
                        if ((hisProduct.SelectedItem.ToString() == HISMBSGX) || (hisProduct.SelectedItem.ToString() == HISFSVGX))
                            setSearchPathTo = "mbs";
                        else if (hisProduct.SelectedItem.ToString() == HISSVAGX)
                            setSearchPathTo = "sva4";
                    }
                    reader.Close();
                    string user = "";
                    pgConnection.createUserAndPasswordString(getHisProduct(), ref user);
                    bool retval = ODBCManager.CreateDSN(entryName, dbServerName, comboBoxEncoding.SelectedItem.ToString(),
                                                        true, dbName, dbServerPort, user, setSearchPathTo);

                    if (!retval)
                    {
                        TextBlockStatus.Text = "fail: ODBCManager.CreateDSN";
                        return;
                    }

                    // add lang to db, if necessary
                    command = new NpgsqlCommand(@"CREATE OR REPLACE FUNCTION make_plpgsql()
                                                            RETURNS VOID
                                                            LANGUAGE SQL
                                                            AS $$
                                                             CREATE TRUSTED PROCEDURAL LANGUAGE 'plpgsql'
                                                             HANDLER plpgsql_call_handler
                                                             VALIDATOR plpgsql_validator;
                                                             ALTER LANGUAGE plpgsql OWNER TO postgres;
                                                            $$;
                                                            SELECT
                                                                CASE
                                                                WHEN EXISTS(
                                                                    SELECT 1
                                                                    FROM pg_catalog.pg_language
                                                                    WHERE lanname='plpgsql'
                                                                )
                                                                THEN NULL
                                                                ELSE make_plpgsql() END;
                                                            DROP FUNCTION make_plpgsql();", con);
                    int returnCode = command.ExecuteNonQuery();
                    
                }
                else if (comboBoxDBType.SelectedItem.ToString() == DBINFORMIX)
                {
                    String host = dbServerName;
                    dbServerName = ((ComboBoxServer)(databaseServers.SelectedItem)).InformixServer;
                }

                // create registry entries
                RegistryManager.CreateEntry(hisProductName, entryName, dbServerName, dbName, comboBoxDBType.SelectedItem.ToString());

                TextBlockStatus.Text = "Datenbank " + dbName + " erfolgreich eingerichtet; Verfügbar über Eintrag " + entryName + ".";
                // reload list for immediate deletion of new database
                comboBox1_Loaded(null, null);
            }
            catch (Exception ex)
            {
                TextBlockStatus.Text = ex.Message;
                return;
            }
        }

        private void databaseServers_Loaded(object sender, RoutedEventArgs e)
        {
            databaseServers.Items.Clear();
            if (comboBoxDBType.SelectedItem == null)
                return;            
            else if (comboBoxDBType.SelectedItem.ToString() == DBPOSTGRES)
            {                
                databaseServers.Items.Add(new ComboBoxServer("localhost", PGPORT));
                databaseServers.Items.Add(new ComboBoxServer("vmpostgres90", PGPORT));
                databaseServers.Items.Add(new ComboBoxServer("vmpostgres91", PGPORT));
                //databaseServers.Items.Add(new ComboBoxServer("his2843", PGPORT));

                checkBoxLoadUserDBs.IsEnabled = true;
                comboBoxEncoding.IsEnabled = true;
            }
            else if (comboBoxDBType.SelectedItem.ToString() == DBINFORMIX)
            {
                //databaseServers.Items.Add(new ComboBoxServer("localhost", PGPORT));                
                databaseServers.Items.Add(new ComboBoxServer("hermes", "1529", "", "hermes_onl9_net"));
                databaseServers.Items.Add(new ComboBoxServer("apollo", "1529", "", "apollo_onl9_net"));
                databaseServers.Items.Add(new ComboBoxServer("vmifx117", "1526", "", "vmifx117_onl11_net"));
                databaseServers.Items.Add(new ComboBoxServer("his2843", "1526", "", "ol_his2843"));
                checkBoxLoadUserDBs.IsChecked = false;
                checkBoxLoadUserDBs.IsEnabled = false;
                comboBoxEncoding.IsEnabled = false;
                comboBoxEncoding.SelectedIndex = -1;
            }
        }

        // This event handler deals with the results of the
        // background operation.
        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // First, handle the case where an exception was thrown.
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.Message);
            }
            else if (e.Cancelled)
            {
                // Next, handle the case where the user canceled
                // the operation.
                // Note that due to a race condition in
                // the DoWork event handler, the Cancelled
                // flag may not have been set, even though
                // CancelAsync was called.
                TextBlockStatus.Text = "Canceled";
            }
            else
            {
                // Finally, handle the case where the operation
                // succeeded.
                //                TextBlockStatus.Text = e.Result.ToString();
            }

            // do remaining work.
            foreach (ComboBoxServer server in userDBs)
            {
                databaseServers.Items.Add(server);
            }
        }

        private void findUserDBs(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            List<Win32.NetApi32.SERVER_INFO_101> serverList = Win32.NetApi32.GetServerList(Win32.NetApi32.SV_101_TYPES.SV_TYPE_ALL);

            foreach (Win32.NetApi32.SERVER_INFO_101 server in serverList)
            {
                if (worker.CancellationPending) break;

                String nameToShow = server.sv101_comment;
                if (nameToShow.Contains("UB1"))
                {
                    // this test takes too long
                    DBConnection pgConnection = new DBConnection(DBConnection.DBType.Postgres);

                    if (pgConnection.openPGConnection(server.sv101_name, "postgres", PGPORT, "", true) != null)
                    {
                        nameToShow = nameToShow.Replace("UB1", "");
                        nameToShow = nameToShow.Trim('-', ' ');

                        Dispatcher.Invoke(new Action(() => { userDBs.Add(new ComboBoxServer(server.sv101_name, PGPORT, nameToShow)); }), System.Windows.Threading.DispatcherPriority.Normal, null);

                    }
                }
            }
        }        

        private void databaseServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (databaseServers.SelectedItem == null)
                return;

            ArrayList sortedDBs = new ArrayList();

            String currentDBServer = ((ComboBoxServer)databaseServers.SelectedItem).ServerName;
            String currentDBServerPort = ((ComboBoxServer)databaseServers.SelectedItem).Port;

            TextBlockStatus.Text = "Verfügbare Datenbanken werden in " + currentDBServer + " gesucht.";

            if (comboBoxDBType.SelectedItem.ToString() == DBPOSTGRES)
            {
                DBConnection pgConnection = new DBConnection(DBConnection.DBType.Postgres);
                NpgsqlConnection sqlConx = (NpgsqlConnection)pgConnection.openPGConnection(currentDBServer, "postgres", currentDBServerPort, getHisProduct(),  false);

                if (sqlConx == null)
                {
                    TextBlockStatus.Text = "Datenbank Verbindung fehlgeschlagen!";
                    return;
                }

                DataTable tblDatabases = sqlConx.GetSchema("Databases");

                sqlConx.Close();

                this.databases.Items.Clear();                

                foreach (DataRow row in tblDatabases.Rows)
                {
                    try
                    {
                        String dbName = row["database_name"].ToString();
                        String dbOwner = row["owner"].ToString();
                        String dbEncoding = row["encoding"].ToString();

                        if ((dbName != "postgres") && (dbName != "template0") && (dbName != "template1") && (dbName != "latin1"))
                        {
                            sortedDBs.Add(dbName.Trim());
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: " + ex.Message);
                        return;
                    }
                }
            }
            else if (comboBoxDBType.SelectedItem.ToString() == DBINFORMIX)
            {

                String currentInformixServer = ((ComboBoxServer)databaseServers.SelectedItem).InformixServer;

                if (!SQLHosts.EntryExists(currentInformixServer))
                {
                    SQLHosts.CreateEntry(currentInformixServer, currentDBServer, currentDBServerPort);
                }

                try
                {
                    DBConnection ifxConnection = new DBConnection(DBConnection.DBType.Informix);

                    IfxConnection conn = (IfxConnection)ifxConnection.openIfxConnection(currentDBServer, currentInformixServer, "sysmaster", currentDBServerPort, getHisProduct(), false);
                    
                    String commandText = "SELECT sysdatabases.name, sysdatabases.owner, sysdbslocale.dbs_collate FROM sysdbslocale INNER JOIN sysdatabases ON sysdatabases.name = sysdbslocale.dbs_dbsname;";

                    IfxDataReader reader = (IfxDataReader)ifxConnection.readQuery(commandText);

                    while ((reader != null) && reader.HasRows && reader.Read())
                    {
                        String dbName = reader[0].ToString();
                        if (!dbName.StartsWith("sys"))
                        {
                            sortedDBs.Add(dbName.Trim());
                        }
                    }

                    conn.Close();

                }
                catch (IfxException ex)
                {
                    MessageBox.Show("Failed opening connection: " + ex);
                    TextBlockStatus.Text = "Datenbank Verbindung fehlgeschlagen!";
                    return;
                }
                catch (TypeInitializationException ex)
                {
                    MessageBox.Show("Failed opening connection: " + ex);
                    TextBlockStatus.Text = "Datenbank Verbindung fehlgeschlagen!";
                    return;
                }

            }

            sortedDBs.Sort();

            this.databases.Items.Clear();
            foreach (String db in sortedDBs)
            {
                this.databases.Items.Add(db);
            }
            TextBlockStatus.Text = "Server " + currentDBServer + " hat " + databases.Items.Count + " Datenbanken verfügbar.";
        }        

        private void hisProduct_Loaded(object sender, RoutedEventArgs e)
        {
            /*
            hisProduct.Items.Add(HISFSVGX);
            hisProduct.Items.Add(HISMBSGX);
            hisProduct.Items.Add(HISSVAGX);
             * */
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            createNewEntry();
        }

        private void comboBox1_Loaded(object sender, RoutedEventArgs e)
        {
            // get all entries, in ODBC and in Registry
            comboBox1.Items.Clear();
            // ODBC
            string[] values = ODBCManager.GetAllDSN();

            if (values == null) return;

            // Registry
            values = values.Concat(RegistryManager.GetAllEntries()).Distinct().ToArray();
            Array.Sort(values);

            foreach (String entry in values)
            {
                comboBox1.Items.Add(entry);
            }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            if (comboBox1.SelectedItem == null)
            {
                MessageBox.Show("Fehlende Eingabe!");
                return;
            }
            String rememberItem = comboBox1.SelectedValue.ToString();
            ODBCManager.RemoveDSN(rememberItem);
            RegistryManager.DeleteEntry(rememberItem);
            // refresh list
            comboBox1_Loaded(null, null);
            TextBlockStatus.Text = "Alle Einträge von \"" + rememberItem + "\" aus der Registry und ODBC Quellen gelöscht.";
        }

        private void comboBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboBox1.SelectedValue != null)
            {
                textBox1.Text = "Treiber: " + ODBCManager.GetValue(comboBox1.SelectedValue.ToString(), "Driver");
            }
            else
            {
                textBox1.Clear();
            }
        }

        private void checkBox1_Checked(object sender, RoutedEventArgs e)
        {
            // don't do anything for informix
            if ((String) comboBoxDBType.SelectedItem == DBINFORMIX)
                return;

            bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(findUserDBs);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker1_RunWorkerCompleted);
            bw.WorkerSupportsCancellation = true;         
            bw.RunWorkerAsync();
        }

        private void checkBox1_Unchecked(object sender, RoutedEventArgs e)
        {
            // kill background worker
            bw.CancelAsync();
        }

        private void comboBoxEncoding_Loaded(object sender, RoutedEventArgs e)
        {
            comboBoxEncoding.Items.Add(PGANSI);
            comboBoxEncoding.Items.Add(PGUNICODE);
        }

        private void comboBoxDBType_Loaded(object sender, RoutedEventArgs e)
        {
            comboBoxDBType.Items.Add(DBPOSTGRES);
            comboBoxDBType.Items.Add(DBINFORMIX);
        }

        private void comboBoxDBType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            databaseServers_Loaded(sender, e);
        }

        private void databases_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DBConnection dbconn = DBConnectionSetup();
            if (dbconn == null) return;
            ArrayList allVersions = dbconn.getGXVersions();

            hisProduct.Items.Clear();
            foreach (Object[] versionPair in allVersions)
            {
                hisProduct.Items.Add(versionPair[0].ToString().Trim() + " (" + versionPair[1].ToString().Trim() + ")");
            }
            TextBlockStatus.Text = hisProduct.Items.Count + " Produktdatenbanken gefunden in " + databases.SelectedValue.ToString().Trim() + ".";

        }

        private DBConnection DBConnectionSetup()
        {
            if ((databases.SelectedItem == null) || (databaseServers.SelectedItem == null))
                return null;
            String dbServerName = ((ComboBoxServer)(databaseServers.SelectedItem)).ServerName;
            String dbServerPort = ((ComboBoxServer)(databaseServers.SelectedItem)).Port;            
            String dbName = databases.SelectedValue.ToString().Trim();

            DBConnection dbconn = new DBConnection((comboBoxDBType.SelectedItem.ToString() == DBPOSTGRES) ? DBConnection.DBType.Postgres : DBConnection.DBType.Informix);

            if (comboBoxDBType.SelectedItem.ToString() == DBPOSTGRES)
            {
                dbconn.openPGConnection(dbServerName, dbName, dbServerPort, getHisProduct(), false);
            }
            else
            {
                String ifxServerName = ((ComboBoxServer)(databaseServers.SelectedItem)).InformixServer;
                dbconn.openIfxConnection(dbServerName, ifxServerName, dbName, dbServerPort, getHisProduct(), false);
            }

            return dbconn;
        }
    }

    public class DBConnection
    {
        public enum DBType
        {
            Informix,
            Postgres
        };

        private DBType currentDBType;
        private DbConnection dbConn;


        public DBConnection(DBType newType)
        {
            currentDBType = newType;
        }

        public DbDataReader readQuery(String query)
        {
            DbCommand command = null;
            if (currentDBType == DBType.Postgres)
                command = new NpgsqlCommand(query, (NpgsqlConnection)dbConn);
            else
                command = new IfxCommand(query, (IfxConnection)dbConn);
            DbDataReader reader = null;
            try
            {
                reader = command.ExecuteReader();
            }
            catch
            {

            }
            return reader;
        }

        public ArrayList getGXVersions()
        {
            if (dbConn == null)
                return null;
            ArrayList allVersions = new ArrayList();
            String versionsQuery;
            DbDataReader reader;

            if (currentDBType == DBType.Postgres)
            {
                String namespaceQuery = "SELECT n.nspname || '.' || c.relname FROM pg_catalog.pg_class c LEFT JOIN pg_namespace n ON n.oid = c.relnamespace " +
                                        "WHERE nspname !~ '^pg_.*|info-*' AND c.relname = 'db_version';";
                reader = (NpgsqlDataReader)readQuery(namespaceQuery);
                
                ArrayList allSchemas = new ArrayList();
                while ((reader != null) && reader.HasRows && reader.Read())
                {
                    String schemaName = reader.GetString(0);
                    allSchemas.Add(schemaName);
                }

                foreach (String currentSchema in allSchemas)
                {
                    versionsQuery = "SELECT his_system, version FROM " + currentSchema + " WHERE kern_system='1';";
                    reader = (NpgsqlDataReader)readQuery(versionsQuery);
                    while (reader != null && reader.HasRows && reader.Read())
                    {
                        Object[] version = new Object[reader.FieldCount];
                        reader.GetValues(version);
                        allVersions.Add(version);
                    }
                }


            }

            else if (currentDBType == DBType.Informix)
            {
                versionsQuery = "SELECT his_system, version FROM db_version WHERE kern_system='1';";

                reader = (IfxDataReader)readQuery(versionsQuery);

                while (reader != null && reader.HasRows && reader.Read())
                {
                    Object[] version = new Object[reader.FieldCount];
                    reader.GetValues(version);
                    allVersions.Add(version);
                }
            }

            return allVersions;


            
            //DbCommand dbcommand = new DbCommand();
            //dbConn.sq
        }

        public String createUserAndPasswordString(String product, ref String user)
        {
            
            if (product == MainWindow.HISSVAGX)
                user = "sva";
            else // HISFSVGX, HISMBSGX or initial contact
                user = "fsv";
            return "User Id=" + user + ";Password=" + user + "." + user + ";";
        }

        public DbConnection openPGConnection(String host, String db, String port, String product, bool silent)
        {
            return openDBConnection(host, "", db, port, product, silent);
        }

        public DbConnection openIfxConnection(String host, String informixServer, String db, String port, String product, bool silent)
        {
            return openDBConnection(host, informixServer, db, port, product, silent);
        }

        private DbConnection openDBConnection(String host, String informixServer, String db, String port, String product, bool silent)
        {
            String user = "", connString;

            if (currentDBType == DBType.Postgres)
            {
                if (db == "") db = "postgres";
                connString = "Server=" + host + ";Port=" + port + ";Integrated Security=true;" + createUserAndPasswordString(product, ref user) + "Database=" + db + ";Timeout=1;CommandTimeout=1;";                
                
            }
            else
            {
                connString = "Database=" + db + ";" + createUserAndPasswordString(product, ref user) +
                                "Host=" + host + ";Server=" + informixServer + ";" +
                                "Service=" + port + ";Protocol=onsoctcp;";                
            }

            try
            {
                if (currentDBType == DBType.Postgres)
                    dbConn = new NpgsqlConnection(connString);
                else
                    dbConn = new IfxConnection(connString);
                dbConn.Open();
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show("Error: " + ex.Message);
                }
                return null;
            }
            return dbConn;
        }
    }

    public class ComboBoxServer
    {
        public string Name { get; set; }

        public string ServerName { get; set; }

        public string Port { get; set; }

        public string InformixServer { get; set; }

        public ComboBoxServer(string servername, string port, string displayname, string informixServer)
        {
            ServerName = servername;
            Port = port;
            if (displayname != "")
                Name = displayname;
            else
                Name = servername;
            InformixServer = informixServer;
        }

        public ComboBoxServer(string servername, string port, string displayname)
        {
            ServerName = servername;
            Port = port;
            Name = displayname;
        }

        public ComboBoxServer(string servername, string port)
        {
            ServerName = servername;
            Port = port;
            Name = servername;
        }

        public ComboBoxServer(string servername)
        {
            ServerName = servername;
            Port = MainWindow.PGPORT;
            Name = servername;
        }
    }

    public static class SQLHosts
    {
        private const string PATH = "SOFTWARE\\Informix\\SqlHosts";

        public static bool EntryExists(String databaseServer)
        {
            var dbKey = Registry.LocalMachine.OpenSubKey(PATH + "\\" + databaseServer);
            return dbKey != null;
        }

        public static void CreateEntry(String InformixServer, String host, String port)
        {
            var dbKey = Registry.LocalMachine.CreateSubKey(PATH + "\\" + InformixServer);
            if (dbKey == null) throw new Exception("Registry key for SQLHosts was not created");
            dbKey.SetValue("HOST", host);
            dbKey.SetValue("PROTOCOL", "onsoctcp");
            dbKey.SetValue("SERVICE", port);
            dbKey.Flush();
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

        public static void CreateEntry(String productName, String entryName, String databaseServer, String databaseName, String databaseType)
        {
            var dbKey = Registry.LocalMachine.CreateSubKey(HIS_REG_PATH + "\\" + productName + "\\" + "Datenbank\\" + entryName);
            if (dbKey == null) throw new Exception("Registry key for DB was not created");
            dbKey.SetValue("DB-Server", databaseServer);            
            dbKey.SetValue("ODBCAutoCommit", 0, RegistryValueKind.DWord);
            dbKey.SetValue("Pruefmodus", 0, RegistryValueKind.DWord);
            if (databaseType == DB2GX.MainWindow.DBPOSTGRES)
            {
                dbKey.SetValue("Name", entryName);
                dbKey.SetValue("Typ", 6, RegistryValueKind.DWord);
            }
            else //DBINFORMIX
            {
                dbKey.SetValue("Name", databaseName);
                dbKey.SetValue("Typ", 1, RegistryValueKind.DWord);
            }
            dbKey.SetValue("Zugriff", 1, RegistryValueKind.DWord);
        }

        public static string[] GetAllEntries()
        {
            String fsvPath = HIS_REG_PATH + MainWindow.HISFSVGX + "\\Datenbank",
                   mbsPath = HIS_REG_PATH + MainWindow.HISMBSGX + "\\Datenbank";

            RegistryKey fsvKey = Registry.LocalMachine.OpenSubKey(fsvPath),
                        mbsKey = Registry.LocalMachine.OpenSubKey(mbsPath);

            if (fsvKey == null)
                Registry.LocalMachine.CreateSubKey(fsvPath);

            if (mbsKey == null)
                Registry.LocalMachine.CreateSubKey(mbsPath);

            string[] returnValues = fsvKey.GetSubKeyNames();
            string[] tempValues = mbsKey.GetSubKeyNames();
            int originalLength = returnValues.Length;
            Array.Resize<string>(ref returnValues, returnValues.Length + tempValues.Length);
            if (tempValues.Length != 0)
                Array.Copy(tempValues, 0, returnValues, originalLength, tempValues.Length - 1);
            return returnValues;
        }

        public static void DeleteEntry(string entry)
        {
            String fullEntry = HIS_REG_PATH + MainWindow.HISFSVGX + "\\Datenbank\\" + entry;
            if (Registry.LocalMachine.OpenSubKey(fullEntry) != null)
                Registry.LocalMachine.DeleteSubKeyTree(fullEntry);

            fullEntry = HIS_REG_PATH + MainWindow.HISMBSGX + "\\Datenbank\\" + entry;
            if (Registry.LocalMachine.OpenSubKey(fullEntry) != null)
                Registry.LocalMachine.DeleteSubKeyTree(fullEntry);
        }
    }

    ///<summary>
    /// Class to assist with creation and removal of ODBC DSN entries
    ///</summary>
    public static class ODBCManager
    {
        private const string ODBC_INI_REG_PATH = "SOFTWARE\\ODBC\\ODBC.INI\\";
        private const string ODBCINST_INI_REG_PATH = "SOFTWARE\\ODBC\\ODBCINST.INI\\";

        public static String GetValue(string dsnName, string key)
        {
            String retval = "";
            RegistryKey dsnKey = Registry.LocalMachine.OpenSubKey(ODBC_INI_REG_PATH + dsnName);
            if (dsnKey != null)
            {
                retval = dsnKey.GetValue(key).ToString();
            }
            return retval;
        }

        /// <summary>
        /// Creates a new DSN entry with the specified values. If the DSN exists, the values are updated.
        /// </summary>
        /// <param name="dsnName">Name of the DSN for use by client applications</param>
        /// <param name="server">Network name or IP address of database server</param>
        /// <param name="driverName">Name of the driver to use</param>
        /// <param name="trustedConnection">True to use NT authentication, false to require applications to supply username/password in the connection string</param>
        /// <param name="database">Name of the datbase to connect to</param>
        /// <param name="port">Port of server</param>
        /// <param name="setConnSettings">Set search_path TO this value, if not empty</param>
        public static bool CreateDSN(string dsnName, string server, string driverName, bool trustedConnection, string database, string port, string user, string setConnSettings)
        {
            // Lookup driver path from driver name
            String driverPath = ODBCINST_INI_REG_PATH + driverName;
            RegistryKey driverKey = Registry.LocalMachine.OpenSubKey(driverPath);
            if (driverKey == null)
            {
                driverKey = Registry.LocalMachine.CreateSubKey(driverPath);
            }
            driverPath = (String)driverKey.GetValue("Driver");
            if (driverPath == null)
            {
                MessageBox.Show("driverPath ist null!");
                return false;
            }

            // Add value to odbc data sources
            RegistryKey datasourcesKey = GetDatasourcesKey();
            datasourcesKey.SetValue(dsnName, driverName);

            // Create new key in odbc.ini with dsn name and add values
            RegistryKey dsnKey = Registry.LocalMachine.CreateSubKey(ODBC_INI_REG_PATH + dsnName);
            if (dsnKey == null) throw new Exception("ODBC Registry key for DSN was not created");
            dsnKey.SetValue("Database", database);
            dsnKey.SetValue("Driver", driverPath);
            dsnKey.SetValue("LastUser", Environment.UserName);
            dsnKey.SetValue("Servername", server);
            dsnKey.SetValue("Port", port);

            dsnKey.SetValue("Database", database);
            dsnKey.SetValue("Trusted_Connection", trustedConnection ? "Yes" : "No");

            // HIS Extras
            dsnKey.SetValue("MaxLongVarcharSize", "32766");
            dsnKey.SetValue("MaxVarcharSize", "32766");
            if (setConnSettings != "")
            {
                dsnKey.SetValue("ConnSettings", "SET+search%5fpath+TO+" + setConnSettings);
            }
            dsnKey.SetValue("Username", "fsv");
            dsnKey.SetValue("Password", "fsv.fsv");
            dsnKey.SetValue("Protocol", "7.4-2");

            return true;
        }

        private static RegistryKey GetDatasourcesKey()
        {
            String datasourcesPath = ODBC_INI_REG_PATH + "ODBC Data Sources";
            RegistryKey datasourcesKey = Registry.LocalMachine.OpenSubKey(datasourcesPath, true);
            if (datasourcesKey == null)
            {
                datasourcesKey = Registry.LocalMachine.CreateSubKey(datasourcesPath);                
            }
            return datasourcesKey;
        }

        public static string[] GetAllDSN()
        {
            RegistryKey datasourcesKey = GetDatasourcesKey();
            if (datasourcesKey == null) return null;
            string[] returnValues = datasourcesKey.GetValueNames();
            Array.Sort(returnValues);
            return returnValues;
        }

        /// <summary>
        /// Removes a DSN entry
        /// </summary>
        /// <param name="dsnName">Name of the DSN to remove.</param>
        public static void RemoveDSN(string dsnName)
        {
            // Remove DSN key
            if (Registry.LocalMachine.OpenSubKey(ODBC_INI_REG_PATH + dsnName) != null)
                Registry.LocalMachine.DeleteSubKeyTree(ODBC_INI_REG_PATH + dsnName);

            // Remove DSN name from values list in ODBC Data Sources key
            RegistryKey datasourcesKey = Registry.LocalMachine.CreateSubKey(ODBC_INI_REG_PATH + "ODBC Data Sources");
            if (datasourcesKey == null) throw new Exception("ODBC Registry key for datasources does not exist");
            if (datasourcesKey.GetValue(dsnName) != null)
                datasourcesKey.DeleteValue(dsnName);
        }

        ///<summary>
        /// Checks the registry to see if a DSN exists with the specified name
        ///</summary>
        ///<param name="dsnName"></param>
        ///<returns></returns>
        public static bool DSNExists(string dsnName)
        {
            RegistryKey dsnKey = Registry.LocalMachine.OpenSubKey(ODBC_INI_REG_PATH + dsnName);

            return dsnKey != null;
        }

        ///<summary>
        /// Returns an array of driver names installed on the system
        ///</summary>
        ///<returns></returns>
        public static string[] GetInstalledDrivers()
        {
            RegistryKey driversKey = Registry.LocalMachine.CreateSubKey(ODBCINST_INI_REG_PATH + "ODBC Drivers");
            if (driversKey == null) throw new Exception("ODBC Registry key for drivers does not exist");

            String[] driverNames = driversKey.GetValueNames();

            List<string> ret = new List<string>();

            foreach (String driverName in driverNames)
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