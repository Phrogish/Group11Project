using System;
using System.Data;
using System.Web.Services;
using MySql.Data.MySqlClient;

namespace ProjectTemplate
{
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    [System.Web.Script.Services.ScriptService]
    public class ProjectServices : System.Web.Services.WebService
    {
        ////////////////////////////////////////////////////////////////////////
        /// replace the values of these variables with your database credentials
        ////////////////////////////////////////////////////////////////////////
        private string dbID = "cis440Spring2026team11";
        private string dbPass = "cis440Spring2026team11";
        private string dbName = "cis440Spring2026team11";
        ////////////////////////////////////////////////////////////////////////

        private string getConString()
        {
            return "SERVER=107.180.1.16; PORT=3306; DATABASE=" + dbName +
                   "; UID=" + dbID + "; PASSWORD=" + dbPass;
        }

        [WebMethod(EnableSession = true)]
        public string TestConnection()
        {
            try
            {
                string testQuery = "SELECT COUNT(*) FROM employees;";

                using (MySqlConnection con = new MySqlConnection(getConString()))
                using (MySqlCommand cmd = new MySqlCommand(testQuery, con))
                {
                    con.Open();
                    cmd.ExecuteScalar();
                }

                return "Success!";
            }
            catch (Exception e)
            {
                return "Something went wrong, please check your credentials and db name and try again. Error: " + e.Message;
            }
        }

        
        // Login Feature
        public class LoginResult
        {
            public bool success { get; set; }
            public string message { get; set; }
            public int userId { get; set; }
            public string role { get; set; }
            public int points { get; set; }
            public bool isLocked { get; set; }
            public int remainingAttempts { get; set; }
        }

        [WebMethod(EnableSession = true)]
        public LoginResult Login(string email, string password)
        {
          LoginResult resp = new LoginResult();

            try
            {
                using (MySqlConnection con = new MySqlConnection(getConString()))
                {
                    con.Open();

                    // 1) Look up user by email
                    string sql = @"
                     SELECT employee_id, password, role, points, failed_attempts, is_locked, last_login_point_date
                     FROM employees
                     WHERE email = @email
                     LIMIT 1;
                     ";

                    int employeeId;
                    string dbPassword;
                    string role;
                    int points;
                    int failedAttempts;
                    bool locked;
                    //checking the date for assigning daily 1 point for logging in
                    DateTime? lastLoginPointDate = null;
                    
                    using (MySqlCommand cmd = new MySqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@email", email);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                resp.success = false;
                                resp.message = "Invalid email or password.";
                                return resp;
                            }

                            employeeId = reader.GetInt32("employee_id");
                            dbPassword = reader.GetString("password");
                            role = reader.GetString("role");
                            points = reader.GetInt32("points");
                            failedAttempts = reader.GetInt32("failed_attempts");
                            locked = reader.GetInt32("is_locked") == 1;

                            int dateIndex = reader.GetOrdinal("last_login_point_date");

                            if (!reader.IsDBNull(dateIndex))
                            {
                                lastLoginPointDate = reader.GetDateTime(dateIndex).Date;
                            }
                        }
                    }

                    // 2) Locked?
                    if (locked)
                    {
                        resp.success = false;
                        resp.isLocked = true;
                        resp.remainingAttempts = 0;
                        resp.message = "Account locked after 3 failed attempts.";
                        return resp;
                    }

                    // 3) Correct password?
                    if (password == dbPassword)
                    {
                        DateTime today = DateTime.Today;

                        bool giveLoginPoint = !lastLoginPointDate.HasValue || lastLoginPointDate.Value < today;

                        if (giveLoginPoint)
                        {
                            using (MySqlCommand award = new MySqlCommand(@"
                        UPDATE employees
                        SET points = points + 1,
                        last_login_point_date = CURDATE(),
                        failed_attempts = 0,
                        is_locked = 0
                        WHERE employee_id = @id;", con))
                            {
                                award.Parameters.AddWithValue("@id", employeeId);
                                award.ExecuteNonQuery();
                            }

                            points += 1; // IMPORTANT so the response shows the updated points
                        }
                        else
                        {
                            // no point today, just reset lock/attempts
                            using (MySqlCommand reset = new MySqlCommand(@"
                            UPDATE employees
                            SET failed_attempts = 0,
                            is_locked = 0
                            WHERE employee_id = @id;", con))
                            {
                                reset.Parameters.AddWithValue("@id", employeeId);
                                reset.ExecuteNonQuery();
                            }
                        }

                        // set session
                        Session["userId"] = employeeId;
                        Session["role"] = role;

                        resp.success = true;
                        resp.message = giveLoginPoint ? "Login successful. +1 daily login point!" : "Login successful.";
                        resp.userId = employeeId;
                        resp.role = role;
                        resp.points = points;
                        resp.remainingAttempts = 3;
                        return resp;
                    }

                    // 4) If Wrong password then increment attempts and possibly lock
                    failedAttempts += 1;
                    bool nowLocked = failedAttempts >= 3;

                    using (MySqlCommand upd = new MySqlCommand(@"
						UPDATE employees
						SET failed_attempts = @fa, is_locked = @locked
						WHERE employee_id = @id;", con))
                    {
                        upd.Parameters.AddWithValue("@fa", failedAttempts);
                        upd.Parameters.AddWithValue("@locked", nowLocked ? 1 : 0);
                        upd.Parameters.AddWithValue("@id", employeeId);
                        upd.ExecuteNonQuery();
                    }

                    resp.success = false;
                    resp.isLocked = nowLocked;
                    resp.remainingAttempts = Math.Max(0, 3 - failedAttempts);
                    resp.message = nowLocked
                        ? "Account locked after 3 failed attempts."
                        : $"Invalid email or password. Attempts remaining: {resp.remainingAttempts}";
                    return resp;
                }
            }
            catch (Exception e)
            {
                resp.success = false;
                resp.message = "Server error: " + e.Message;
                return resp;
            }
        }
        //making a class for Me result section
        public class MeResult
        {
            public bool loggedIn { get; set; }
            public int userId { get; set; }
            public string role { get; set; }
        }
        //backend session check for user
        [WebMethod(EnableSession = true)]
        public MeResult Me()
        {
            if (Session["userId"] == null)
            {
                return new MeResult
                {
                    loggedIn = false,
                    userId = 0,
                    role = ""
                };
            }

            return new MeResult
            {
                loggedIn = true,
                userId = (int)Session["userId"],
                role = (string)Session["role"]
            };
        }
        //showcasing points after successful login 
        [WebMethod(EnableSession = true)]
        public int GetPoints()
        {
            if (Session["userId"] == null)
                throw new Exception("Not logged in");

            using (MySqlConnection con = new MySqlConnection(getConString()))
            {
                con.Open();

                MySqlCommand cmd = new MySqlCommand(
                    "SELECT points FROM employees WHERE employee_id = @id", con);

                cmd.Parameters.AddWithValue("@id", Session["userId"]);

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        [WebMethod(EnableSession = true)]
        public bool Logout()
        {
            Session.Clear();
            Session.Abandon();
            return true;
        }
    }
}