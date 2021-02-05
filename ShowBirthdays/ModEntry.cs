﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace ShowBirthdays
{
	class ModEntry : Mod
	{
		//private bool changeOnClick = false;
		//private bool cycleAlways = false;
		//private bool cycleHover = true;
		private CycleType cycleType;
		// Flag for if the calendar is open
		private bool calendarOpen = false;
		// Interval in ticks for changing the birthday sprite
		private int spriteCycleTicks = 120;
		private int currentCycle = 0;

		private int clickedDay = -1;
		private Vector2 cursorPos = new Vector2();

		// List of triplets (Season, Day, NPC)
		//private readonly List<Tuple<String, int, NPC>> birthdays = new List<Tuple<string, int, NPC>>();

		//private readonly Dictionary<String, BirthdayHelper> birthdays = new Dictionary<string, BirthdayHelper>();

		private BirthdayHelper bdHelper;
		private ModConfig config;


		public override void Entry(IModHelper helper)
		{
			helper.Events.Display.MenuChanged += OnMenuChanged;
			helper.Events.Display.RenderingActiveMenu += OnRenderingActiveMenu;
			helper.Events.GameLoop.GameLaunched += OnLaunched;
			helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
			helper.Events.Input.ButtonPressed += OnButtonPressed;
			helper.Events.Input.CursorMoved += OnCursorMoved;
		}

		private void OnLaunched(object sender, GameLaunchedEventArgs e)
		{
			config = Helper.ReadConfig<ModConfig>();
			var api = Helper.ModRegistry.GetApi<GenericModConfigAPI>("spacechase0.GenericModConfigMenu");

			if (api != null)
			{
				api.RegisterModConfig(ModManifest, () => config = new ModConfig(), () => Helper.WriteConfig(config));

				api.RegisterClampedOption(ModManifest, "Cycle duration", "Duration in draw cycles", () => config.cycleDuration, (int val) => config.cycleDuration = val, 1, 600);
				api.RegisterChoiceOption(ModManifest, "Cycle type", "How the sprites cycle", () => config.cycleType, (string val) => config.cycleType = val, ModConfig.cycleTypes);
			}
		}

		private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
		{
			// Initialize the helper
			bdHelper = new BirthdayHelper(this);

			// Read the config options
			if (config.cycleType.Equals("Always"))
				cycleType = CycleType.Always;
			else if (config.cycleType.Equals("Hover"))
				cycleType = CycleType.Hover;
			else if (config.cycleType.Equals("Click"))
				cycleType = CycleType.Click;
			else
			{
				PrintLogMessage("The only accepted cycle types are Always, Hover and Click. Defaulting to Always.", LogLevel.Error);
				cycleType = CycleType.Always;
			}

			spriteCycleTicks = config.cycleDuration;
			if (spriteCycleTicks < 1)
			{
				PrintLogMessage("Cycle duration can't be less than 1", LogLevel.Error);
				spriteCycleTicks = 1;
			}

			foreach (NPC n in Utility.getAllCharacters())
			{
				// Checking for 0 should eliminate a lot of the non-friendable NPCs
				// Tarkista onko season null, entä dwarf, sandy ja krobus? entä asocial npcs (marlon kahteen kertaan SVE:n kanssa)?
				if (n.isVillager() && n.Birthday_Day > 0)
				{
					//Tuple<String, int, NPC> entry = Tuple.Create(n.Birthday_Season, n.Birthday_Day, n);
					//birthdays.Add(entry);
					bdHelper.AddBirthday(n.Birthday_Season, n.Birthday_Day, n);
				}
			}
		}


		/// <summary>
		/// Raised after a game menu is opened, closed, or replaced.
		/// Edit the calendar's hover texts to include extra birthdays.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void OnMenuChanged(object sender, MenuChangedEventArgs e)
		{
			if (e.NewMenu == null)
			{
				calendarOpen = false;
				return;
			}

			if (e.NewMenu is Billboard billboard)
			{
				bool dailyQuests = this.Helper.Reflection.GetField<bool>(billboard, "dailyQuestBoard").GetValue();

				// Do nothing if looking at the quest board
				if (dailyQuests)
					return;

				// We are looking at the calendar so update the flag for it
				calendarOpen = true;

				List<ClickableTextureComponent> days = billboard.calendarDays;

				// List of all the birthdays for the season
				List<int> list = bdHelper.GetDays(Game1.currentSeason);

				// NOTE: Remember that i goes from 1 to 28, so substract 1 from it to use as the index!
				for (int i = 1; i <= days.Count; i++)
				{
					// Build the hover text from festival, birthdays and wedding if applicable
					string newHoverText = "";

					// Add the festival text if needed
					if (Utility.isFestivalDay(i, Game1.currentSeason))
					{
						// Festival name hover text from base game
						newHoverText = Game1.temporaryContent.Load<Dictionary<string, string>>(string.Concat("Data\\Festivals\\" + Game1.currentSeason, i))["name"];
					}
					else if (Game1.currentSeason.Equals("winter") && i >= 15 && i <= 17)
					{
						// Night Market hover text from base game
						newHoverText = Game1.content.LoadString("Strings\\UI:Billboard_NightMarket");
					}

					// Get the list of all NPCs with the birthday and add them to the hover text
					List<NPC> listOfNPCs = bdHelper.GetNpcs(Game1.currentSeason, i);

					if (listOfNPCs != null)
					{
						for (int j = 0; j < listOfNPCs.Count; j++)
						{
							if (!newHoverText.Equals(""))
							{
								newHoverText += Environment.NewLine;
							}

							NPC n = listOfNPCs[j];
							// Build the hover text just like in the base game. I'm not touching that.
							newHoverText += ((n.displayName.Last() != 's' && (LocalizedContentManager.CurrentLanguageCode != LocalizedContentManager.LanguageCode.de || (n.displayName.Last() != 'x' && n.displayName.Last() != 'ß' && n.displayName.Last() != 'z'))) ? Game1.content.LoadString("Strings\\UI:Billboard_Birthday", n.displayName) : Game1.content.LoadString("Strings\\UI:Billboard_SBirthday", n.displayName));
						}
					}

					// Get a refrence to the list of weddings
					IReflectedField<Dictionary<ClickableTextureComponent, List<string>>> weddings = this.Helper.Reflection.GetField<Dictionary<ClickableTextureComponent, List<string>>>(billboard, "_upcomingWeddings");

					// Wedding text from base game
					if (weddings.GetValue().ContainsKey(days[i - 1]))
					{
						for (int j = 0; j < weddings.GetValue().Count / 2; j++)
						{
							if (!newHoverText.Equals(""))
							{
								newHoverText += Environment.NewLine;
							}

							newHoverText += Game1.content.LoadString("Strings\\UI:Calendar_Wedding", weddings.GetValue()[days[i - 1]][j * 2], weddings.GetValue()[days[i - 1]][j * 2 + 1]);
						}
					}

					days[i - 1].hoverText = newHoverText.Trim();


					// Add the textures
					if (list.Contains(i))
					{
						days[i - 1].texture = bdHelper.GetCurrentSprite(Game1.currentSeason, i);
					}
				}
			}
		}


		private void OnRenderingActiveMenu(object sender, RenderingActiveMenuEventArgs e)
		{
			if (!calendarOpen)
				return;

			if (currentCycle < spriteCycleTicks)
			{
				currentCycle++;
			}

			// Possibly dangerous conversion
			List<ClickableTextureComponent> days = (Game1.activeClickableMenu as Billboard).calendarDays;

			List<int> listOfDays = bdHelper.GetDays(Game1.currentSeason);

			switch (cycleType)
			{
				case CycleType.Always:
					if (currentCycle == spriteCycleTicks)
					{
						for (int i = 0; i < listOfDays.Count; i++)
						{
							try
							{
								days[listOfDays[i] - 1].texture = bdHelper.GetNextSpriteForDay(Game1.currentSeason, listOfDays[i]);
							}
							catch
							{
								// Generic error for now.
								PrintLogMessage("There was a problem with parsing the birthday data", LogLevel.Error);
							}
						}

						// Reset the cycle
						currentCycle = 0;
					}
					break;
				case CycleType.Hover:
					if (currentCycle == spriteCycleTicks)
					{
						for (int i = 0; i < listOfDays.Count; i++)
						{
							if (days[listOfDays[i] - 1].containsPoint((int)cursorPos.X, (int)cursorPos.Y))
							{
								days[listOfDays[i] - 1].texture = bdHelper.GetNextSpriteForDay(Game1.currentSeason, listOfDays[i]);
							}
						}

						// Reset the cycle
						currentCycle = 0;
					}
					break;
				case CycleType.Click:
					if (clickedDay != -1 && listOfDays.Contains(clickedDay))
					{
						days[clickedDay - 1].texture = bdHelper.GetNextSpriteForDay(Game1.currentSeason, clickedDay);
						clickedDay = -1;
					}
					break;
				default:
					PrintLogMessage("Unknown cycle type encountered in OnRenderingActiveMenu", LogLevel.Error);
					break;
			}

			
		}


		private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
		{
			if (!calendarOpen || cycleType != CycleType.Click)
				return;

			if (e.Button == SButton.MouseLeft)
			{
				// Dangerous conversion, but should work for now
				List<ClickableTextureComponent> days = (Game1.activeClickableMenu as Billboard).calendarDays;

				for (int i = 0; i < days.Count; i++)
				{
					Vector2 point = e.Cursor.GetScaledScreenPixels();

					if (days[i].containsPoint((int)point.X, (int)point.Y))
					{
						clickedDay = i + 1;
						Monitor.Log("Player clicked on day " + clickedDay + " at " + point.ToString(), LogLevel.Debug);
					}
				}
			}
		}


		private void OnCursorMoved(object sender, CursorMovedEventArgs e)
		{
			if (cycleType != CycleType.Hover || !calendarOpen)
				return;

			cursorPos = e.NewPosition.GetScaledScreenPixels();
		}


		public void PrintLogMessage(string s, LogLevel level)
		{
			Monitor.Log(s, level);
		}


		class BirthdayHelper
		{
			// Reference to the outer class to allow error logging
			private readonly ModEntry mod;
			public List<Birthday>[] birthdays = new List<Birthday>[4];


			public BirthdayHelper(ModEntry mod)
			{
				this.mod = mod;

				// Initialize the array of lists
				for (int i = 0; i < birthdays.Length; i++)
				{
					birthdays[i] = new List<Birthday>();
				}
			}


			internal void AddBirthday(string season, int birthday, NPC n)
			{
				List<Birthday> list = GetListOfBirthdays(season);

				if (list == null)
				{
					mod.PrintLogMessage("Failed to add birthday " + season + birthday.ToString() + " for " + n.Name, LogLevel.Error);
					return;
				}

				// null if birthday hasn't been added
				Birthday day = list.Find(x => x.day == birthday);

				if (day == null)
				{
					// Add the birthday
					Birthday newDay = new Birthday(mod, birthday);
					newDay.AddNPC(n);
					list.Add(newDay);
				}
				else
				{
					// Add the npc to the existing day
					day.AddNPC(n);
				}
			}


			public List<int> GetDays(string season)
			{
				List<Birthday> list = GetListOfBirthdays(season);

				List<int> days = new List<int>();

				if (list != null)
				{
					for (int i = 0; i < list.Count; i++)
					{
						days.Add(list[i].day);
					}
				}

				return days;
			}


			/// <summary>
			/// Returns the list of NPCs that have the given birthday. Returns null if no NPC matches the date.
			/// </summary>
			internal List<NPC> GetNpcs(string season, int day)
			{
				Birthday birthday = GetBirthday(season, day);

				if (birthday == null)
					return null;
				else
					return birthday.GetNPCs();
			}


			private Birthday GetBirthday(string season, int day)
			{
				List<Birthday> list = GetListOfBirthdays(season);

				if (list == null)
					return null;

				return list.Find(x => x.day == day);
			}


			/// <summary>
			/// Returns the list of Birthdays for the given season. Returns null if such list was not found.
			/// </summary>
			private List<Birthday> GetListOfBirthdays(string season)
			{
				try
				{
					int index = Utility.getSeasonNumber(season);
					return birthdays[index];
				}
				catch (Exception)
				{
					mod.PrintLogMessage("Index problems", LogLevel.Error);
					return null;
				}

			}

			internal Texture2D GetNextSpriteForDay(string season, int day)
			{
				Birthday birthday = GetBirthday(season, day);
				return birthday.GetNextSprite();
			}


			internal Texture2D GetCurrentSprite(string season, int day)
			{
				Birthday birthday = GetBirthday(season, day);
				return birthday.GetCurrentSprite();
			}
		}


		class Birthday
		{
			// The day
			public int day;
			// Every NPC that has a birthday that day
			private List<NPC> npcs = new List<NPC>();

			private int currentSpriteIndex = 0;
			// Reference to the outer class to allow error logging
			private readonly ModEntry mod;

			public Birthday(ModEntry mod, int day)
			{
				this.mod = mod;
				this.day = day;
			}


			public List<NPC> GetNPCs(bool hideUnmet = true)
			{
				List<NPC> list = new List<NPC>();

				for (int i = 0; i < npcs.Count; i++)
				{
					//if (Game1.player.friendshipData.ContainsKey(npcs[i].Name))
					if (npcs[i].CanSocialize)
					{
						list.Add(npcs[i]);
					}
				}

				return list;
			}


			public void AddNPC(NPC n)
			{
				npcs.Add(n);
			}


			internal Texture2D GetNextSprite()
			{
				NPC n;

				List<NPC> list = GetNPCs();

				if (list.Count == 0)
				{
					return null;
				}

				// Increment the index and loop it back to 0 if we reached the end
				currentSpriteIndex++;
				if (currentSpriteIndex == list.Count)
					currentSpriteIndex = 0;

				try
				{
					//n = npcs[currentSpriteIndex];
					n = list[currentSpriteIndex];
				}
				catch (Exception)
				{
					mod.PrintLogMessage("Getting the NPC from the index failed", LogLevel.Error);
					return null;
				}
				
				Texture2D texture;

				// How the base game handles getting the sprite
				try
				{
					texture = Game1.content.Load<Texture2D>("Characters\\" + n.getTextureName());
				}
				catch (Exception)
				{
					texture = n.Sprite.Texture;
				}

				mod.PrintLogMessage("Sprite changed from " + (currentSpriteIndex == 0 ? list[list.Count - 1].Name : list[currentSpriteIndex - 1].Name) + " to " + list[currentSpriteIndex].Name, LogLevel.Trace);

				return texture;
			}


			internal Texture2D GetCurrentSprite()
			{
				NPC n;

				List<NPC> list = GetNPCs();

				// There is no texture if there is no NPCs
				if (list.Count == 0)
				{
					return null;
				}

				try
				{
					n = list[currentSpriteIndex];
				}
				catch (Exception)
				{
					mod.PrintLogMessage("Getting the NPC from the index failed", LogLevel.Error);
					return null;
				}

				Texture2D texture;

				// How the base game handles getting the sprite
				try
				{
					texture = Game1.content.Load<Texture2D>("Characters\\" + n.getTextureName());
				}
				catch (Exception)
				{
					texture = n.Sprite.Texture;
				}

				return texture;
			}
		}


		class ModConfig
		{
			public int cycleDuration = 120;
			public string cycleType = "Always";
			internal static string[] cycleTypes = new string[] { "Always", "Hover", "Click" };
		}

		
		enum CycleType
		{
			Always,
			Hover,
			Click
		}
	}


	public interface GenericModConfigAPI
	{
		void RegisterModConfig(IManifest mod, Action revertToDefault, Action saveToFile);

		void RegisterClampedOption(IManifest mod, string optionName, string optionDesc, Func<int> optionGet, Action<int> optionSet, int min, int max);
		void RegisterChoiceOption(IManifest mod, string optionName, string optionDesc, Func<string> optionGet, Action<string> optionSet, string[] choices);
	}
}