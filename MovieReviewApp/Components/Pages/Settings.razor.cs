using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Rendering;
using MovieReviewApp.Database;
using MovieReviewApp.Models;
using System;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MovieReviewApp.Components.Pages
{
    public partial class Settings
    {
        [Inject]
        private MongoDb db { get; set; } = default!;


        public List<Person>? People { get; set; }
        public DateTime? StartDate { get; set; }
        public int? TimeCount;
        public string? TimePeriod;
        public string NewPerson = "New Person";
        public bool RespectOrder = false;
        public List<Setting> settings = new List<Setting>();
        public readonly List<SelectListItem> TimePeriods = new List<SelectListItem>
        {
            new SelectListItem { Value = "Month", Text = "Month" },
            new SelectListItem { Value = "Week", Text = "Week" },
            new SelectListItem { Value = "Day", Text = "Day" }
        };

        protected override void OnInitialized()
        {
            settings = db.GetSettings();

            RespectOrder = false; // default value
            var setting = settings.FirstOrDefault(x => x.Key == "RespectOrder");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                bool.TryParse(setting.Value, out RespectOrder);
            StartDate = DateTime.Parse(settings.First(x => x.Key == "StartDate").Value);
            TimeCount = int.Parse(settings.First(x => x.Key == "TimeCount").Value);
            TimePeriod = settings.First(x => x.Key == "TimePeriod").Value;

            People = db.GetAllPeople(RespectOrder);
        }


        private void AddPerson()
        {
            db.AddPerson(new Person { Name = NewPerson });
            NewPerson = "New Person";
            People = db.GetAllPeople(RespectOrder);
        }

        private void Edit(Person person)
        {
            if (person != null)
                person.IsEditing = true;
        }
        private void Cancel(Person person)
        {
            if (person != null)
                person.IsEditing = false;
        }
        private void Delete(Person person)
        {
            if (person != null)
            {
                db.DeletePerson(person);
                People = db.GetAllPeople(RespectOrder);
            }
        }
        private void Save(Person person)
        {
            if (person != null)
            {
                db.AddOrUpdatePerson(person);
                this.Cancel(person);
            }
        }
        private void SaveDate()
        {
            var dateSetting = settings.First(x => x.Key == "StartDate");
            dateSetting.Value = StartDate.ToString();
            db.AddOrUpdateSetting(dateSetting);
        }
        private void SaveOccurance(Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
        {
            var timeCountSetting = settings.First(x => x.Key == "TimeCount");
            timeCountSetting.Value = TimeCount.ToString();
            db.AddOrUpdateSetting(timeCountSetting);
            
            var timePeriodSetting = settings.First(x => x.Key == "TimePeriod");
            timePeriodSetting.Value = TimePeriod;
            db.AddOrUpdateSetting(timePeriodSetting);
        }
    }
}