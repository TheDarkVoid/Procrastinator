﻿using Nancy.Hosting.Self;
using Procrastinator.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Procrastinator
{
	class Program
	{
		static void Main(string[] args)
		{
			var host = new NancyHost(new HostConfiguration() { UrlReservations = new UrlReservations() { CreateAutomatically = true } }, new Uri("http://localhost:2410"));
			host.Start();
			Console.WriteLine("Hosting...");
			ProcrastinatorCore.Init();
			Console.ReadLine();
		}
	}
}
