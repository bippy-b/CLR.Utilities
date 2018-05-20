using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

public partial class CLRUtilities
{
    // Validate input string against regular expression
    [SqlFunction(IsDeterministic = true, DataAccess = DataAccessKind.None)]
    public static SqlBoolean fn_RegExMatch(SqlString inpStr,
      SqlString regExStr)
    {
        if (inpStr.IsNull || regExStr.IsNull)
            return SqlBoolean.Null;
        else
            return (SqlBoolean)Regex.IsMatch(inpStr.Value, regExStr.Value,
              RegexOptions.CultureInvariant);
    }

    // SQL Signature
    [SqlFunction(IsDeterministic = true, DataAccess = DataAccessKind.None)]
    public static SqlString fn_SQLSigCLR(SqlString inpRawString,
      SqlInt32 inpParseLength)
    {
        if (inpRawString.IsNull)
            return SqlString.Null;
        int pos = 0;
        string mode = "command";
        string RawString = inpRawString.Value;
        int maxlength = RawString.Length;
        StringBuilder p2 = new StringBuilder();
        char currchar = ' ';
        char nextchar = ' ';
        int ParseLength = RawString.Length;
        if (!inpParseLength.IsNull)
            ParseLength = inpParseLength.Value;
        if (RawString.Length > ParseLength)
        {
            maxlength = ParseLength;
        }
        while (pos < maxlength)
        {
            currchar = RawString[pos];
            if (pos < maxlength - 1)
            {
                nextchar = RawString[pos + 1];
            }
            else
            {
                nextchar = RawString[pos];
            }
            if (mode == "command")
            {
                p2.Append(currchar);
                if ((",( =<>!".IndexOf(currchar) >= 0)
                   &&
                    (nextchar >= '0' && nextchar <= '9'))
                {
                    mode = "number";
                    p2.Append('#');
                }
                if (currchar == '\'')
                {
                    mode = "literal";
                    p2.Append("#'");
                }
            }
            else if ((mode == "number")
                      &&
                       (",( =<>!".IndexOf(nextchar) >= 0))
            {
                mode = "command";
            }
            else if ((mode == "literal") && (currchar == '\''))
            {
                mode = "command";
            }
            pos++;
        }
        return p2.ToString();
    }

    // fn_RegExReplace - for generic use of RegEx-based replace
    [SqlFunction(IsDeterministic = true, DataAccess = DataAccessKind.None)]
    public static SqlString fn_RegExReplace(
        SqlString input, SqlString pattern, SqlString replacement)
    {
        if (input.IsNull || pattern.IsNull || replacement.IsNull)
            return SqlString.Null;
        else
            return (SqlString)Regex.Replace(
            input.Value, pattern.Value, replacement.Value);
    }

    // Compare implicit vs. explicit casting
    [SqlFunction(IsDeterministic = true, DataAccess = DataAccessKind.None)]
    public static string fn_ImpCast(string inpStr)
    {
        return inpStr.Substring(2, 3);
    }

    [SqlFunction(IsDeterministic = true, DataAccess = DataAccessKind.None)]
    public static SqlString fn_ExpCast(SqlString inpStr)
    {
        return (SqlString)inpStr.ToString().Substring(2, 3);
    }

    // Struct used in string split functions
    struct row_item
    {
        public string item;
        public int pos;
    }

    // Split array of strings and return a table
    // FillRowMethodName = "ArrSplitFillRow"
    [SqlFunction(FillRowMethodName = "ArrSplitFillRow",
     DataAccess = DataAccessKind.None,
     TableDefinition = "pos INT, element NVARCHAR(4000) ")]
    public static IEnumerable fn_SplitCLR(SqlString inpStr,
        SqlString charSeparator)
    {
        string locStr;
        string[] splitStr;
        char[] locSeparator = new char[1];
        locSeparator[0] = (char)charSeparator.Value[0];
        if (inpStr.IsNull)
            locStr = "";
        else
            locStr = inpStr.Value;
        splitStr = locStr.Split(locSeparator,
              StringSplitOptions.RemoveEmptyEntries);
        //locStr.Split(charSeparator.ToString()[0]);
        List<row_item> SplitString = new List<row_item>();
        int i = 1;
        foreach (string s in splitStr)
        {
            row_item r = new row_item();
            r.item = s;
            r.pos = i;
            SplitString.Add(r);
            ++i;
        }
        return SplitString;
    }

    public static void ArrSplitFillRow(
      Object obj, out int pos, out string item)
    {
        pos = ((row_item)obj).pos;
        item = ((row_item)obj).item;
    }

    // Stored procedure that returns environment info in tabular format
    [SqlProcedure]
    public static void usp_GetEnvInfo()
    {
        // Create a record - object representation of a row
        // Include the metadata for the SQL table
        SqlDataRecord record = new SqlDataRecord(
            new SqlMetaData("EnvProperty", SqlDbType.NVarChar, 20),
            new SqlMetaData("Value", SqlDbType.NVarChar, 256));
        // Marks the beginning of the result set to be sent back to the client
        // The record parameter is used to construct the metadata
        // for the result set
        SqlContext.Pipe.SendResultsStart(record);
        // Populate some records and send them through the pipe
        record.SetSqlString(0, @"Machine Name");
        record.SetSqlString(1, Environment.MachineName);
        SqlContext.Pipe.SendResultsRow(record);
        record.SetSqlString(0, @"Processors");
        record.SetSqlString(1, Environment.ProcessorCount.ToString());
        SqlContext.Pipe.SendResultsRow(record);
        record.SetSqlString(0, @"OS Version");
        record.SetSqlString(1, Environment.OSVersion.ToString());
        SqlContext.Pipe.SendResultsRow(record);
        record.SetSqlString(0, @"CLR Version");
        record.SetSqlString(1, Environment.Version.ToString());
        SqlContext.Pipe.SendResultsRow(record);
        // End of result set
        SqlContext.Pipe.SendResultsEnd();
    }

    // Stored procedure that returns assembly info
    // uses Reflection
    [SqlProcedure]
    public static void usp_GetAssemblyInfo(SqlString asmName)
    {
        // Retrieve the clr name of the assembly
        String clrName = null;
        // Get the context
        using (SqlConnection connection =
                 new SqlConnection("Context connection = true"))
        {
            connection.Open();
            using (SqlCommand command = new SqlCommand())
            {
                // Get the assembly and load it
                command.Connection = connection;
                command.CommandText =
                  "SELECT clr_name FROM sys.assemblies WHERE name = @asmName";
                command.Parameters.Add("@asmName", SqlDbType.NVarChar);
                command.Parameters[0].Value = asmName;
                clrName = (String)command.ExecuteScalar();
                if (clrName == null)
                {
                    throw new ArgumentException("Invalid assembly name!");
                }
                Assembly myAsm = Assembly.Load(clrName);
                // Create a record - object representation of a row
                // Include the metadata for the SQL table
                SqlDataRecord record = new SqlDataRecord(
                    new SqlMetaData("Type", SqlDbType.NVarChar, 50),
                    new SqlMetaData("Name", SqlDbType.NVarChar, 256));
                // Marks the beginning of the result set to be sent back
                // to the client
                // The record parameter is used to construct the metadata
                // for the result set
                SqlContext.Pipe.SendResultsStart(record);
                // Get all types in the assembly
                Type[] typesArr = myAsm.GetTypes();
                foreach (Type t in typesArr)
                {
                    // Type in a SQL database should be a class or
                    // a structure
                    if (t.IsClass == true)
                    {
                        record.SetSqlString(0, @"Class");
                    }
                    else
                    {
                        record.SetSqlString(0, @"Structure");
                    }
                    record.SetSqlString(1, t.FullName);
                    SqlContext.Pipe.SendResultsRow(record);
                    // Find all public static methods
                    MethodInfo[] miArr = t.GetMethods();
                    foreach (MethodInfo mi in miArr)
                    {
                        if (mi.IsPublic && mi.IsStatic)
                        {
                            record.SetSqlString(0, @"  Method");
                            record.SetSqlString(1, mi.Name);
                            SqlContext.Pipe.SendResultsRow(record);
                        }
                    }
                }
                // End of result set
                SqlContext.Pipe.SendResultsEnd();
            }
        }
    }

    // Generic trigger for auditing DML statements
    // trigger will write first 200 characters from all columns
    // in an XML format to App Event Log
    [SqlTrigger(Name = @"trg_GenericDMLAudit", Target = "T1",
       Event = "FOR INSERT, UPDATE, DELETE")]
    public static void trg_GenericDMLAudit()
    {
        // Get the trigger context to get info about the action type
        SqlTriggerContext triggContext = SqlContext.TriggerContext;
        // Prepare the command and pipe objects
        SqlCommand command;
        SqlPipe pipe = SqlContext.Pipe;

        // Check whether the action is Insert
        switch (triggContext.TriggerAction)
        {
            case TriggerAction.Insert:
                // Retrieve the connection that the trigger is using
                using (SqlConnection connection
                   = new SqlConnection(@"context connection=true"))
                {
                    connection.Open();
                    // Collect all columns into an XML type, cast it
                    // to nvarchar and select only a substring from it
                    // Info from Inserted
                    command = new SqlCommand(
                      @"SELECT 'New data: '
                        + SUBSTRING(CAST(a.InsertedContents AS NVARCHAR(MAX))
                            ,1,200) AS InsertedContents200 
                          FROM (SELECT * FROM Inserted FOR XML AUTO, TYPE)
                               AS a(InsertedContents);",
                       connection);
                    // Store info collected to a string variable
                    string msg;
                    msg = (string)command.ExecuteScalar();
                    // Write the audit info to the event log
                    EventLogEntryType entry = new EventLogEntryType();
                    entry = EventLogEntryType.SuccessAudit;
                    // Note: if the following line would use
                    // Environment.MachineName instead of "." to refer to
                    // the local machine event log, the assembly would need
                    // the UNSAFE permission set
                    EventLog ev = new EventLog(@"Application",
                      ".", @"GenericDMLAudit Trigger");
                    ev.WriteEntry(msg, entry);
                    // send the audit info to the user
                    pipe.Send(msg);
                }
                break;
            case TriggerAction.Update:
                // Retrieve the connection that the trigger is using
                using (SqlConnection connection
                   = new SqlConnection(@"context connection=true"))
                {
                    connection.Open();
                    // Collect all columns into an XML type,
                    // cast it to nvarchar and select only a substring from it
                    // Info from Deleted
                    command = new SqlCommand(
                      @"SELECT 'Old data: '
                        + SUBSTRING(CAST(a.DeletedContents AS NVARCHAR(MAX))
                            ,1,200) AS DeletedContents200 
                          FROM (SELECT * FROM Deleted FOR XML AUTO, TYPE)
                               AS a(DeletedContents);",
                       connection);
                    // Store info collected to a string variable
                    string msg;
                    msg = (string)command.ExecuteScalar();
                    // Info from Inserted
                    command.CommandText =
                      @"SELECT ' // New data: '
                        + SUBSTRING(CAST(a.InsertedContents AS NVARCHAR(MAX))
                            ,1,200) AS InsertedContents200 
                          FROM (SELECT * FROM Inserted FOR XML AUTO, TYPE)
                               AS a(InsertedContents);";
                    msg = msg + (string)command.ExecuteScalar();
                    // Write the audit info to the event log
                    EventLogEntryType entry = new EventLogEntryType();
                    entry = EventLogEntryType.SuccessAudit;
                    EventLog ev = new EventLog(@"Application",
                      ".", @"GenericDMLAudit Trigger");
                    ev.WriteEntry(msg, entry);
                    // send the audit info to the user
                    pipe.Send(msg);
                }
                break;
            case TriggerAction.Delete:
                // Retrieve the connection that the trigger is using
                using (SqlConnection connection
                   = new SqlConnection(@"context connection=true"))
                {
                    connection.Open();
                    // Collect all columns into an XML type,
                    // cast it to nvarchar and select only a substring from it
                    // Info from Deleted
                    command = new SqlCommand(
                      @"SELECT 'Old data: '
                        + SUBSTRING(CAST(a. DeletedContents AS NVARCHAR(MAX))
                            ,1,200) AS DeletedContents200 
                          FROM (SELECT * FROM Deleted FOR XML AUTO, TYPE)
                               AS a(DeletedContents);",
                       connection);
                    // Store info collected to a string variable
                    string msg;
                    msg = (string)command.ExecuteScalar();
                    // Write the audit info to the event log
                    EventLogEntryType entry = new EventLogEntryType();
                    entry = EventLogEntryType.SuccessAudit;
                    EventLog ev = new EventLog(@"Application",
                      ".", @"GenericDMLAudit Trigger");
                    ev.WriteEntry(msg, entry);
                    // send the audit info to the user
                    pipe.Send(msg);
                }
                break;
            default:
                // Just to be sure - this part should never fire
                pipe.Send(@"Nothing happened");
                break;
        }
    }

};