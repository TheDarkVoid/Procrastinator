﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using Procrastinator.Credentials;
using Procrastinator.Models;
using Nancy.Authentication.Stateless;
using Nancy.Security;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Procrastinator
{
	public static class ProcrastinatorCore
	{
		public const string HOST = "kaisei.app";


		private static Dictionary<string, IUserIdentity> loggedInUsers = new Dictionary<string, IUserIdentity>();

		internal static StatelessAuthenticationConfiguration StatelessConfig { get; private set; } = new StatelessAuthenticationConfiguration(nancyContext =>
		{
			try
			{
				string ApiKey = nancyContext.Request.Cookies.First(c => c.Key == "Auth").Value;
				Console.WriteLine($"AuthKey KEY: {ApiKey}");
				return GetUserFromAuthKey(ApiKey);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return null;
			}
		});

		/// <summary>
		/// Initializes the Core
		/// </summary>
		/// <returns>Wheather initializtion was sucessful</returns>
		public static bool Init()
		{
			return true;
		}

		internal static bool CheckUserExists(string username)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT username FROM {DBCredentials.DB_userTable} WHERE username = '{Uri.EscapeDataString(username)}'";
					return cmd.ExecuteReader().HasRows;
				}
			}
		}

		/// <summary>
		/// Opens a new DB connection
		/// </summary>
		/// <returns>The new connection to the DB</returns>
		public static NpgsqlConnection GetConnection()
		{
			var con = new NpgsqlConnection(DBCredentials.CONNECTION_STRING);
			con.Open();
			return con;
		}

		#region Validation
		private static string HashPassword(string password)
		{
			byte[] salt;
			new RNGCryptoServiceProvider().GetBytes(salt = new byte[16]);

			var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000);
			byte[] hash = pbkdf2.GetBytes(20);

			byte[] hashBytes = new byte[36];
			Array.Copy(salt, 0, hashBytes, 0, 16);
			Array.Copy(hash, 0, hashBytes, 16, 20);

			return Convert.ToBase64String(hashBytes);
		}

		private static bool VerifyPassword(string password, string passwordHash)
		{
			if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
				return false;
			/* Extract the bytes */
			byte[] hashBytes = Convert.FromBase64String(passwordHash);
			/* Get the salt */
			byte[] salt = new byte[16];
			Array.Copy(hashBytes, 0, salt, 0, 16);
			/* Compute the hash on the password the user entered */
			var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000);
			byte[] hash = pbkdf2.GetBytes(20);
			/* Compare the results */
			for (int i = 0; i < 20; i++)
				if (hashBytes[i + 16] != hash[i])
					return false;
			return true;
		}

		public static string GenerateUserAuthToken()
		{
			return Guid.NewGuid().ToString();
		}

		public static long GenerateID()
		{
			return Guid.NewGuid().ToByteArray().GetHashCode(); //TODO: Better Identifier
		}

		public static string ValidateUser(LoginCredentialsModel user)   /* Switch the empty string to authorization thingie :) */
		{
			if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.Password))
				return null;
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT password FROM {DBCredentials.DB_userTable} WHERE username = '{Uri.EscapeDataString(user.Username)}'";
					string passhash = (string)cmd.ExecuteScalar();
					if (VerifyPassword(user.Password, passhash))
					{
						var auth = GenerateUserAuthToken();
						loggedInUsers.Add(auth, GetUser(user.Username));
						return auth;
					}
					else
						return null;
				}
			}
		}
		private static IUserIdentity GetUserFromAuthKey(string apiKey) => loggedInUsers[apiKey];
		#endregion

		#region Events
		/// <summary>
		/// Retrieves a list of events for a specified month
		/// </summary>
		/// <param name="year">Year of the event</param>
		/// <param name="month">Month of the event</param>
		/// <returns>list of events</returns>
		public static Event[] GetEventsFromMonth(int year, int month, long userID)
		{
			DateTime date = new DateTime(year, month, 1);
			List<Event> events = new List<Event>();
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_eventTable} WHERE userid = '{userID}' AND eventDate >='{date.ToShortDateString()}'::date AND eventDate <'{date.AddMonths(1).ToShortDateString()}'::date";
					using (var reader = cmd.ExecuteReader())
					{
						if (!reader.HasRows)
							return events.ToArray();
						while (reader.Read())
						{
							long[] stickerIds = reader.GetValue(6) as long[];
							events.Add(new Event
							{
								Id = reader.GetInt64(4),
								UserId = reader.GetInt64(7),
								Name = reader.GetString(0),
								Date = reader.GetDateTime(1),
								EndDate = reader.GetDateTime(5),
								AllDay = reader.GetBoolean(2),
								Description = reader.GetString(3),
								Style = (Event.EventStyle)Enum.Parse(typeof(Event.EventStyle), reader.GetString(9), true),
								Color = reader.GetString(8),
								Stickers = GetStickers(stickerIds)
							});
						}
						return events.ToArray();
					}
				}
			}
		}

		/// <summary>
		/// Get a list of events for a specified day
		/// </summary>
		/// <param name="year">Year fo the event</param>
		/// <param name="month">Month of the event</param>
		/// <param name="day">Day of the event</param>
		/// <returns>List of events</returns>
		public static Event[] GetEventsFromDay(int year, int month, int day, long userId)
		{
			DateTime date = new DateTime(year, month, day);
			List<Event> events = new List<Event>();

			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_eventTable} WHERE userid = '{userId}' AND eventDate >='{date.ToShortDateString()}'::date AND eventDate < '{date.AddDays(1).ToShortDateString()}'::date";
					using (var reader = cmd.ExecuteReader())
					{
						if (!reader.HasRows)
							return events.ToArray();
						while (reader.Read())
						{
							long[] stickerIds = reader.GetValue(6) as long[];
							events.Add(new Event
							{
								Id = reader.GetInt64(4),
								UserId = reader.GetInt64(7),
								Name = Uri.UnescapeDataString(reader.GetString(0)),
								Date = reader.GetDateTime(1),
								EndDate = reader.GetDateTime(5),
								AllDay = reader.GetBoolean(2),
								Description = Uri.UnescapeDataString(reader.GetString(3)),
								Style = (Event.EventStyle)Enum.Parse(typeof(Event.EventStyle), reader.GetString(9), true),
								Color = Uri.UnescapeDataString(reader.GetString(8)),
								Stickers = GetStickers(stickerIds)
							});
						}
						return events.ToArray();
					}
				}
			}
		}

		/// <summary>
		/// Creates and new Event in the dB
		/// </summary>
		/// <param name="theEvent">The event to be added</param>
		/// <returns>Returns the event with it's ID filled in</returns>
		public static Event CreateEvent(Event theEvent)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					try
					{
						theEvent.Id = GenerateID();
						cmd.CommandText = $"INSERT INTO {DBCredentials.DB_eventTable} (eventname,eventDate, allday, eventdescription, eventid, eventenddate, stickers, eventstyle, userid, color)  " +
							$" VALUES('{Uri.EscapeDataString(theEvent.Name)}', '{theEvent.Date}', '{theEvent.AllDay}', '{Uri.EscapeDataString(theEvent.Description ?? "")}', '{theEvent.Id}', '{theEvent.EndDate}', '{theEvent.Stickers.ToArrayLiteral()}', '{theEvent.Style}', '{theEvent.UserId}', '{Uri.EscapeDataString(theEvent.Color ?? "")}')";
						cmd.ExecuteNonQuery();
					}catch(Exception e)
					{
						Console.Write(e.StackTrace);
					}
					return theEvent;
				}
			}
		}

		/// <summary>
		/// Retreive an event by ID
		/// </summary>
		/// <param name="id">The id of the event</param>
		/// <returns>The retrieved event</returns>
		public static Event GetEvent(long id)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_eventTable} WHERE eventId='{id}'";
					using (var reader = cmd.ExecuteReader())
					{
						reader.Read();
						long[] stickerIds = reader.GetValue(6) as long[];
						return new Event
						{
							Id = id,
							UserId = reader.GetInt64(7),
							Name = Uri.UnescapeDataString(reader.GetString(0)),
							Date = reader.GetDateTime(1),
							EndDate = reader.GetDateTime(5),
							AllDay = reader.GetBoolean(2),
							Description = Uri.UnescapeDataString(reader.GetString(3)),
							Style = (Event.EventStyle)Enum.Parse(typeof(Event.EventStyle), reader.GetString(9), true),
							Color = Uri.UnescapeDataString(reader.GetString(8)),
							Stickers = GetStickers(stickerIds)
						};
					}
				}
			}
		}

		/// <summary>
		/// Get All Events in the DB
		/// </summary>
		/// <returns>A list of all events</returns>
		public static Event[] GetAllEvents()
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_eventTable}";
					using (var reader = cmd.ExecuteReader())
					{
						if (!reader.HasRows)
							return null;
						while (reader.Read())
						{
							cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_eventTable}";
							List<Event> events = new List<Event>();
							reader.Read();
							long[] stickerIds = reader.GetValue(6) as long[];
							events.Add(new Event
							{
								Id = reader.GetInt64(4),
								UserId = reader.GetInt64(7),
								Name = Uri.UnescapeDataString(reader.GetString(0)),
								Date = reader.GetDateTime(1),
								EndDate = reader.GetDateTime(5),
								AllDay = reader.GetBoolean(2),
								Description = Uri.UnescapeDataString(reader.GetString(3)),
								Style = (Event.EventStyle)Enum.Parse(typeof(Event.EventStyle), reader.GetString(9), true),
								Color = Uri.UnescapeDataString(reader.GetString(8)),
								Stickers = GetStickers(stickerIds)
							});
							return events.ToArray();
						}
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Remove an Event by id
		/// </summary>
		/// <param name="id">The id of the event to be removed</param>
		public static void RemoveEvent(long id)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"DELETE FROM {DBCredentials.DB_eventTable} WHERE eventid = '{id}'";
					cmd.ExecuteNonQuery();
				}
			}
		}
		#endregion

		#region Stickers
		/// <summary>
		/// Create a sticker
		/// </summary>
		/// <param name="sticker">Sticker to be created</param>
		/// <returns>Sticker with it's ID filled in</returns>
		public static Sticker CreateSticker(Sticker sticker)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					sticker.Id = GenerateID();
					cmd.CommandText = $"INSERT INTO {DBCredentials.DB_stickerTable} VALUES ('{sticker.Id}', '{Uri.EscapeDataString(sticker.FileUrl)}', '{sticker.UserId}', '{Uri.EscapeDataString(sticker.Name)}')";
					cmd.ExecuteNonQuery();
					return sticker;
				}
			}
		}

		/// <summary>
		/// Retreive a Sticker from the DB
		/// </summary>
		/// <param name="id">ID of the sticker to be retreived</param>
		/// <returns>The retreived Sticker</returns>
		public static Sticker GetSticker(long id)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_stickerTable} WHERE stickerId='{id}'";
					using (var reader = cmd.ExecuteReader())
					{
						reader.Read();
						return new Sticker
						{
							Id = id,
							Name = Uri.UnescapeDataString(reader.GetString(3)),
							FileUrl = Uri.UnescapeDataString(reader.GetString(1)),
							UserId = reader.GetInt64(2)
						};

					}
				}
			}
		}

		/// <summary>
		/// Retrieve a list of stickers
		/// </summary>
		/// <param name="ids">List of sticker IDs</param>
		/// <returns>List of Stickers retreived</returns>
		public static Sticker[] GetStickers(long[] ids)
		{
			if (ids == null)
				return null;
			if (ids.Length == 0)
				return null;
			List<Sticker> stickers = new List<Sticker>();
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					foreach (long id in ids)
					{
						cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_stickerTable} WHERE stickerId='{id}'";
						using (var reader = cmd.ExecuteReader())
						{
							if (!reader.HasRows)
								continue;
							reader.Read();
							stickers.Add(new Sticker
							{
								Id = id,
								Name = reader.GetString(3),
								FileUrl = reader.GetString(1)
							});
						}
					}
				}
			}
			return stickers.ToArray();
		}

		/// <summary>
		/// Remove a sticker
		/// </summary>
		/// <param name="id">ID of the Sticker</param>
		public static void RemoveSticker(long id)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"DELETE FROM {DBCredentials.DB_stickerTable} WHERE stickerid = {id}";
					cmd.ExecuteNonQuery();
				}
			}
		}
		#endregion STICKER

		#region Users
		/// <summary>
		/// Create a new User
		/// </summary>
		/// <param name="user">User info</param>
		/// <returns>Created User</returns>
		public static User CreateUser(LoginCredentialsModel user)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					User newUser = new User(user.Username)
					{
						Id = GenerateID()
					};
					cmd.CommandText = $"INSERT INTO {DBCredentials.DB_userTable} VALUES ('{newUser.Id}', '{Uri.EscapeDataString(user.Username)}', '{HashPassword(user.Password)}', '{{{string.Join(",", newUser.Claims)}}}')";
					cmd.ExecuteNonQuery();
					return newUser;
				}
			}
		}

		/// <summary>
		/// Retreive a User
		/// </summary>
		/// <param name="id">The User ID</param>
		/// <returns>The retreived User</returns>
		public static User GetUser(long id)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_userTable} WHERE userId='{id}'";
					using (var reader = cmd.ExecuteReader())
					{
						reader.Read();
						return new User(Uri.UnescapeDataString(reader.GetString(1)))
						{
							Id = id,
							UserName = Uri.UnescapeDataString(reader.GetString(1)),
							Claims = (string[])reader.GetValue(3)
						};
					}
				}
			}
		}

		/// <summary>
		/// Retreive a User
		/// </summary>
		/// <param name="id">The Username</param>
		/// <returns>The retreived User</returns>
		public static User GetUser(string username)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {DBCredentials.DB_userTable} WHERE username='{Uri.EscapeDataString(username)}'";
					using (var reader = cmd.ExecuteReader())
					{
						reader.Read();
						return new User(Uri.UnescapeDataString(reader.GetString(1)))
						{
							Id = reader.GetInt64(0),
							UserName = username,
							Claims = (string[])reader.GetValue(3)
						};
					}
				}
			}
		}

		/// <summary>
		/// Remove a User
		/// </summary>
		/// <param name="id">User's ID</param>
		public static void RemoveUser(long id)
		{
			using (var con = GetConnection())
			{
				using (var cmd = con.CreateCommand())
				{
					cmd.CommandText = $"DELETE FROM {DBCredentials.DB_userTable} WHERE userid = '{id}'";
					cmd.ExecuteNonQuery();
				}
			}
		}
		#endregion
	}
}
