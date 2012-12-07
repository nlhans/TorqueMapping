using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimTelemetry.Game.Rfactor;
using Triton.Joysticks;

namespace ThrottleHelper
{
    class Program
    {
        private static Dictionary<double, Dictionary<double, double>> ThrottleMap;
        static void Main(string[] args)
        {
            ThrottleMap = new Dictionary<double, Dictionary<double, double>>();
            var throttles = new List<double>(new double[] {0,0, 10, 20, 30, 40, 50, 60, 70, 80 , 90 , 100});

            string[] data = File.ReadAllLines("../../../Binaries/Map.csv");

            foreach(string line in data)
            {
                string[] cols = line.Split(',');
                double rpm = double.Parse(cols[0]);
                var map = new Dictionary<double, double>();

                for (int i = 1; i < cols.Length; i++)
                    map.Add(throttles[i]/100.0, double.Parse(cols[i])/100.0);

                ThrottleMap.Add(rpm, map);
            }

            StringBuilder test = new StringBuilder();
            for (double rpm =10000; rpm <= 18000; rpm+= 10)
            {
                test.Append(rpm);
                for (double throttle = 50; throttle<=50; throttle+= 1)
                {
                    double t = GetThrottle(rpm/18000, throttle/100);
                    test.Append("," + t);
                }
                test.AppendLine();
            }

            File.WriteAllText("out.csv", test.ToString());

            var rfactor = new Simulator();

            rfactor.Initialize();
            
            var g25s = JoystickDevice.Search("G25");
            if(g25s.Count == 1)
            {
                double p = 0;
                JoystickDevice g25d = g25s[0];
                VirtualJoystick ppjoy1 = new VirtualJoystick(VirtualJoystick.PPJOY_1);
                Joystick g25 = new Joystick(g25d);
                System.Threading.Thread.Sleep(50);
                bool FunnyRevLimit = false;
                while(true)
                {
                    List<double> a = new List<double>();
                    for (int i = 0; i < 24; i++)
                    {
                        a.Add(g25.GetAxis(i));
                    }
                    double throttle = 1 - (g25.GetAxis(2) / ((float)0xFFFF));
                    
                    double rpm = rfactor.Player.Engine_RPM / 0.10471666666666666666666666666667;
                    double rpm_max = rfactor.Player.Engine_RPM_Max_Live / 0.10471666666666666666666666666667;
                    double rpm_funny = rpm_max - 500;
                    int gear = rfactor.Player.Gear;

                    double rpm_factor = rpm/rpm_max;

                    if (FunnyRevLimit)
                    {
                        if (rpm > rpm_funny)
                            throttle = 0;
                    }
                    else
                    {

                        if (rpm_factor >= 1)
                            throttle = 0;
                    }

                    throttle = GetThrottle(rpm_factor, throttle);


                    List<double> axis = new List<double>();
                    axis.Add(throttle);
                    for(int i = 0; i < 7; i++) axis.Add(0);
                    List<bool> buttons = new List<bool>();
                    for(int i = 0; i < 16; i++) buttons.Add(false);
                    //axis[0] = p;
                    p = 1 - p;
                    ppjoy1.PostData(axis, buttons);
                    System.Threading.Thread.Sleep(5);
                    if (FunnyRevLimit && rpm > rpm_funny)
                        System.Threading.Thread.Sleep(75);
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey();

                        if(FunnyRevLimit)
                            Console.WriteLine("Funny rev limit OFF");
                        else
                        {
                            Console.WriteLine("Funny rev limit ON");
                        }
                        FunnyRevLimit = !FunnyRevLimit;
                    }

                }

            }
        }

        private static double GetThrottle(double rpm_factor, double throttle)
        {
            var map1 = new Dictionary<double, double>();
            var map2 = new Dictionary<double, double>();
            var prevmap = ThrottleMap.FirstOrDefault();
            double duty_y = 0;
            foreach(var d in ThrottleMap)
            {

                if (d.Key >= rpm_factor && prevmap.Key <= rpm_factor)
                {
                    duty_y = (rpm_factor - prevmap.Key) / (d.Key - prevmap.Key);
                    map1 = d.Value;
                    map2 = prevmap.Value;
                    break;
                }
                prevmap = d;
            }

            // INTERPOLATE FUCKING SHIT
            var prevthrottle = map1.FirstOrDefault();
            var percents1 = new double[2] {0, 0};
            var percents2 = new double[2] {0, 0};
            double duty_x = 0;
            foreach(var d in map1)
            {
                if (prevthrottle.Key <= throttle && throttle <= d.Key && prevthrottle.Key != 0)
                {
                    duty_x = (throttle - prevthrottle.Key)/(d.Key - prevthrottle.Key);
                    percents1[0] = prevthrottle.Value;
                    percents1[1] = d.Value;
                    break;
                }
                prevthrottle = d;
            }
            foreach(var d in map2)
            {
                if (prevthrottle.Key <= throttle && throttle <= d.Key && prevthrottle.Key != 0)
                {
                    percents2[0] = prevthrottle.Value;
                    percents2[1] = d.Value;
                    break;
                }
                prevthrottle = d;
            }


            // OKAY DO FUCKING THROTTLE MAPPING
            double factor = duty_y*(duty_x*percents1[1] + (1 - duty_x)*percents1[0])
                            + (1 - duty_y)*(duty_x*percents2[1] + (1 - duty_x)*percents2[0]);

            if (throttle*factor >= 2)
                factor = throttle;
            if(factor<=0)
                factor = throttle;
            if (double.IsNaN(factor) || double.IsInfinity(factor))
                factor = throttle;
            Console.WriteLine(String.Format("{0:0.0000} -> {1:0.0000}", throttle, factor));
            throttle = factor;
            return throttle;
        }
    }
}
