using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace AmpelHeinz
{
    public enum AmpelState
    {
        Red,
        Yellow,
        Green,
        AllOn,
        Off
    }

    class Program
    {
        static void Main(string[] args)
        {
            String ampelHeinzVersionString = "0.9";
            String[] jobNames = ConfigurationManager.AppSettings["JobNames"].Split(',');
            String buildServerURL = ConfigurationManager.AppSettings["BuildServerURL"];
            String beagleBoardUrl = ConfigurationManager.AppSettings["BeagleBoardURL"];

            Console.WriteLine("Ampel-Heinz " + ampelHeinzVersionString + " gestartet:");
            Console.WriteLine("Job Namen: " + ConfigurationManager.AppSettings["JobNames"]);
            Console.WriteLine("BuildServer URL: " + buildServerURL);
            Console.WriteLine("BeagleBoard URL: " + beagleBoardUrl);

            var ampelColorResult = GetState(buildServerURL, jobNames);

            bool redOn = ampelColorResult == AmpelState.Red || ampelColorResult == AmpelState.AllOn;
            bool yellowOn = ampelColorResult == AmpelState.Yellow || ampelColorResult == AmpelState.AllOn;
            bool greenOn = ampelColorResult == AmpelState.Green || ampelColorResult == AmpelState.AllOn;

            Console.WriteLine("Versuche folgende Werte auf die Ampel API auf " + beagleBoardUrl + " zu schreiben: " +
                "Rot: " + redOn.ToString() + "; Gelb: " + yellowOn.ToString() + "; Grün: " + greenOn.ToString());

            beagleBoardUrl = beagleBoardUrl + "/?red=" + redOn + "&yellow=" + yellowOn + "&green=" + greenOn;


            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(beagleBoardUrl);
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    Console.WriteLine("Ampelstatus auf \"" + beagleBoardUrl + "\" erfolgreich auf \"" + ampelColorResult + "\" gesetzt");
                    Console.WriteLine(reader.ReadToEnd());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Fehler beim Schreiben des Ampelstatus auf " + beagleBoardUrl + ":\r\n" + e);
            }
            Console.WriteLine("Ampel-Heinz " + ampelHeinzVersionString + " beendet");
        }

        /// <summary>
        /// Gibt den Ampelstatus zu einem Buildserver für bestimmte Jobs zurück:
        /// -Wenn der Color Wert eines der übergebenen Jobs mit "anime" endet wird Gelb zurückgegeben
        /// -Wenn alle Color Werte der übergebenen Jobs "blue" sind, wird Grün zurückgegeben.
        /// -Ansonsten wird Rot zurückgegeben, außer im Fehlerfall
        /// Im Fehlerfall wird AllOn zurückgegeben.
        /// Fehlerfall bedeutet:
        ///     -der Buildserver ist nicht erreichbar
        ///     -es kann keiner der übergebenen Jobs gefunden werden
        ///     -die XML Antwort kann nicht geparsed werden
        /// </summary>
        /// <param name="buildServerUrl">die URL des Buildservers, die XML zurückgibt</param>
        /// <param name="jobNames">eine Liste mit Namen der Jobs</param>
        /// <returns></returns>
        static AmpelState GetState(string buildServerUrl, String[] jobNames)
        {
            AmpelState stateResult = AmpelState.Off;

            Console.WriteLine("Versuche XML Daten vom Buildserver \"" + buildServerUrl + "\" zu lesen");
            XmlDocument xd = new XmlDocument();
            try
            {
                xd.Load(buildServerUrl);
                Console.WriteLine("Daten erfolgreich gelesen");
            }
            catch (Exception e)
            {
                Console.WriteLine("Buildserver \"" + buildServerUrl + "\" nicht erreichbar, oder fehlerhaft:\r\n" + e);
                return AmpelState.AllOn;
            }


            try
            {
                Console.WriteLine("Parse XML Daten");
                var jobNodes = from XmlNode node in xd.DocumentElement.SelectNodes("job")
                               where jobNames.Contains(node.SelectSingleNode("name").InnerText)
                               select node.SelectSingleNode("color").InnerText;

                if (jobNodes.Count() == 0)
                {
                    Console.WriteLine("Es wurden keine Jobs mit den angegebenen Namen auf dem Buildserver \"" + buildServerUrl + "\" gefunden.");
                    return AmpelState.AllOn;
                }

                bool containsAnime = jobNodes.ToList().Any(jn => jn.EndsWith("anime"));
                bool allBlue = jobNodes.ToList().All(jn => jn == "blue");

                //höchste Prio
                if (containsAnime)
                {
                    stateResult = AmpelState.Yellow;
                }
                else if (allBlue)
                {
                    stateResult = AmpelState.Green;
                }
                else
                {
                    stateResult = AmpelState.Red;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Fehler beim Parsen der Buildserver XML Daten:\r\n" + e);
                stateResult = AmpelState.AllOn;
            }

            return stateResult;
        }
    }
}
