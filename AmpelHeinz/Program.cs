﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
        AllOff
    }

    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            Console.CancelKeyPress += Console_CancelKeyPress;
            bool repeat = false;
            if (bool.TryParse(ConfigurationManager.AppSettings["Repeat"], out repeat) && repeat)
            {
                int delay = 0;
                int.TryParse(ConfigurationManager.AppSettings["DelayBetweenRunsInMS"], out delay);
                while (true)
                {
                    doAmpelHeinz();
                    Console.WriteLine("Warte " + delay.ToString() + " ms bis zum nächsten Start von Ampel-Heinz");
                    Thread.Sleep(delay);
                }
            }
            else
            {
                doAmpelHeinz();
            }
        }

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            String beagleBoardUrl = ConfigurationManager.AppSettings["BeagleBoardURL"];
            sendSignalsToBeagleBoard(beagleBoardUrl, false, false, false, AmpelState.AllOff);
        }

        static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            String beagleBoardUrl = ConfigurationManager.AppSettings["BeagleBoardURL"];
            sendSignalsToBeagleBoard(beagleBoardUrl, false, false, false, AmpelState.AllOff);
        }

        private static void doAmpelHeinz()
        {
            String ampelHeinzVersionString = "0.99";
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

            sendSignalsToBeagleBoard(beagleBoardUrl, redOn, yellowOn, greenOn, ampelColorResult);

            Console.WriteLine("Ampel-Heinz " + ampelHeinzVersionString + " beendet");
        }

        private static void sendSignalsToBeagleBoard(String beagleBoardUrl, bool redOn, bool yellowOn, bool greenOn, AmpelState ampelColorResult)
        {
            Console.WriteLine("Versuche folgende Werte auf die Ampel API auf " + beagleBoardUrl + " zu schreiben: " +
               "Rot: " + redOn.ToString().ToLower() + "; Gelb: " + yellowOn.ToString().ToLower() + "; Grün: " + greenOn.ToString().ToLower());

            beagleBoardUrl = beagleBoardUrl + "/?red=" + redOn.ToString().ToLower() + "&yellow=" + yellowOn.ToString().ToLower() + "&green=" + greenOn.ToString().ToLower();


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
            AmpelState stateResult = AmpelState.AllOff;

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

                if (containsAnime) //höchste Prio
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
