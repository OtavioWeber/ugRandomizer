using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Configuration;
using System.IO;
using System.Diagnostics;
using OpenQA.Selenium.Interactions;
using ugRandomizer.Model;
using System.Web.Script.Serialization;
using Newtonsoft.Json;

namespace ugRandomizer
{
    class Program
    {
        public static IWebDriver Driver;
        public static IJavaScriptExecutor js;
        public static EnumAction CurrAction;
        public static string ugURL;
        public static string favPath;
        public static string ugUsername;
        public static string ugPassword;
        public static bool autoScrollEnabled;
        public static bool UserProfileEnabled;
        public static string UserProfilePath;
        public static List<Chord> chords;
        public static string configsPath;
        public static string jsonPath;
        public static Random rng;

        static void Main(string[] args)
        {
            while (CurrAction != EnumAction.Exit)
            {
                switch (CurrAction)
                {
                    case EnumAction.Start:
                        CurrAction = Start();
                        break;
                    case EnumAction.Login:
                        CurrAction = Login();
                        break;
                    case EnumAction.Get:
                        CurrAction = OpenFavorite();
                        break;
                    case EnumAction.Idle:
                        CurrAction = GetUserAction();
                        break;
                    default:
                        break;
                }
            }
            Driver.Quit();
            Environment.Exit(0);
        }

        public static EnumAction Start()
        {
            Console.WriteLine("Initializing...");
            LoadParameters();
            var options = new ChromeOptions();
            options.AddArguments("start-maximized");
            if (UserProfileEnabled)
            {
                options.AddArguments("user-data-dir=" + UserProfilePath);
                options.AddArguments("new-window");
            }
            else
            {
                options.AddUserProfilePreference("credentials_enable_service", false);
            }
            try
            {
                Driver = new ChromeDriver(Path.Combine(Environment.CurrentDirectory + @"\drivers\"), options);
                js = (IJavaScriptExecutor)Driver;
                chords = new List<Chord>();
                rng = new Random();
                return EnumAction.Login;
            }
            catch (Exception ex)
            {
                Console.Write("Error: " + ex.Message);
                return EnumAction.Idle;
            }
        }

        public static void LoadParameters()
        {
            Console.WriteLine("Loading Parameters...");
            ugURL = ConfigurationManager.AppSettings["ugURL"];
            favPath = ConfigurationManager.AppSettings["favPath"];
            ugUsername = ConfigurationManager.AppSettings["username"];
            ugPassword = ConfigurationManager.AppSettings["password"];
            autoScrollEnabled = (ConfigurationManager.AppSettings["autoScrollEnabled"].Equals("true"));
            UserProfileEnabled = (ConfigurationManager.AppSettings["UserProfileEnabled"].Equals("true"));
            UserProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + ConfigurationManager.AppSettings["UserProfilePath"];
            configsPath = Environment.CurrentDirectory + "\\configs";
            jsonPath = configsPath + "\\chords.json";
        }

        public static EnumAction Login()
        {
            var signIn = By.CssSelector("a.js-auth-sign-in-btn");
            var username = By.CssSelector(".auth--js-form-sign-in > input.ug-auth--input[name='username']");
            var password = By.CssSelector(".auth--js-form-sign-in > input.ug-auth--input[name='password']");
            var submit = By.CssSelector("input[type='submit'][value='Sign in']");
            var userHeader = By.CssSelector("#username_header");

            try
            {
                Driver.Navigate().GoToUrl(ugURL);

                if (!WaitForElementLoad(userHeader, 1))
                {
                    Console.WriteLine("Logging in...");
                    Driver.FindElement(signIn).Click();
                    if (WaitForElementLoad(submit, 1))
                    {
                        Driver.FindElement(username).SendKeys(ugUsername);
                        Driver.FindElement(password).SendKeys(ugPassword);
                        Driver.FindElement(submit).Click();
                    }
                }
                WaitForElementLoad(userHeader, 10);

                Console.WriteLine("Logged in as {0}.", Driver.FindElement(userHeader).Text);

                return EnumAction.Get;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return EnumAction.Login;
            }
        }

        public static EnumAction? ReadJSONFile()
        {
            try
            {
                if (!Directory.Exists(configsPath))
                    Directory.CreateDirectory(configsPath);

                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var days = Convert.ToInt32((DateTime.Now - File.GetLastWriteTime(jsonPath)).TotalDays);
                    if (json.Length > 0 && days <= 30)
                        chords = JsonConvert.DeserializeObject<List<Chord>>(json);
                    if (chords.Count > 0)
                    {
                        Console.WriteLine("File opened with {0} chords from {1} days ago.", chords.Count, days);
                        return EnumAction.Get;
                    }
                }
            }
            catch { return null; }
            return null;
        }

        public static EnumAction OpenFavorite()
        {
            var ratingWhy = By.CssSelector("a.modal-link.js-rate-layer-expand");
            var ratingNeverAgain = By.CssSelector(".modal-link.js-rate-layer-never");
            var result = EnumAction.Get;

            if (chords.Count == 0)
            {
                result = ReadJSONFile() ?? GetFavorites();
            }

            if (result == EnumAction.Get)
            {
                try
                {
                    var n = rng.Next(0, chords.Count - 1);

                    Console.WriteLine(string.Format("Opening {0} by {1}...", chords[n].Name, chords[n].Artist));
                    Driver.Navigate().GoToUrl(chords[n].URL);

                    if (WaitForElementLoad(ratingWhy, 1))
                    {
                        // closes an annoying popup if existing
                        Driver.FindElement(ratingWhy).Click();
                        Driver.FindElement(ratingNeverAgain).Click();
                        Console.WriteLine("Popup closed.");
                    }

                    if (autoScrollEnabled)
                    {
                        // sends the "+" key to begin auto scroll
                        for (var i = 0; i < 3; i++)
                            js.ExecuteScript("(function() {var e = new Event('keydown'); e.which = e.keyCode = 107; document.dispatchEvent(e); })();");
                    }
                    return EnumAction.Idle;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                    return EnumAction.Login;
                }
            }
            else
            {
                return result;
            }
        }

        public static EnumAction GetFavorites()
        {
            var showAll = By.CssSelector("li.js-link[data-type='all'] a");
            var favoritesRow = By.CssSelector("tr.tr__lg.tr__actionable.js-favorite");
            var scroll = By.CssSelector("#scroll_holder");
            var ByTd = By.TagName("td");
            var ByA = By.TagName("a");

            try
            {
                Console.WriteLine("Opening favorites list...");
                Driver.Navigate().GoToUrl(ugURL + favPath);

                if (!Driver.Url.Equals(ugURL + favPath))
                    return EnumAction.Login;

                WaitForElementLoad(showAll, 10);
                Driver.FindElement(showAll).Click();

                var chordsList = Driver.FindElements(favoritesRow);

                Console.WriteLine("Saving favorites list...");
                for (var i = 0; i < chordsList.Count; i++)
                {
                    var chord = new Chord();
                    chord.Artist = chordsList[i].FindElements(ByTd)[0].Text;
                    chord.Name = chordsList[i].FindElements(ByTd)[1].Text;
                    chord.URL = chordsList[i].FindElements(ByTd)[1].FindElement(ByA).GetAttribute("href").ToString();
                    chords.Add(chord);
                    Console.Write("\r{0}/{1}", i + 1, chordsList.Count);
                }

                var json = JsonConvert.SerializeObject(chords.ToArray());
                File.WriteAllText(jsonPath, json);

                if (chords.Count > 0)
                {
                    Console.WriteLine(" saved.");
                    return EnumAction.Get;
                }
                else
                {
                    Console.WriteLine("No favorites found.");
                    return EnumAction.Idle;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return EnumAction.Login;
            }
        }

        public static EnumAction GetUserAction()
        {
            Console.WriteLine("Commands:");
            Console.WriteLine("N - Next");
            Console.WriteLine("U - Update File");
            Console.WriteLine("R - Restart App [alpha]");
            Console.WriteLine("E - Exit");
        ReadCommand:
            switch (Console.ReadKey().KeyChar.ToString().ToUpper())
            {
                case "N":
                    Console.WriteLine();
                    return EnumAction.Get;
                case "U":
                    File.WriteAllText(jsonPath, string.Empty);
                    chords.Clear();
                    Console.WriteLine("\nUpdating...");
                    return EnumAction.Get;
                case "R":
                    Console.WriteLine("\nRestarting...");
                    return EnumAction.Start;
                case "E":
                    Console.WriteLine("\nClosing app...");
                    return EnumAction.Exit;
                default:
                    Console.WriteLine("\nUnknown command...");
                    goto ReadCommand;
            }
        }

        public static Boolean WaitForElementLoad(By by, int timeoutInSeconds)
        {
            if (timeoutInSeconds > 0)
            {
                try
                {
                    WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(timeoutInSeconds));
                    wait.Until(ExpectedConditions.ElementIsVisible(by));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
                return false;
        }
    }

}
