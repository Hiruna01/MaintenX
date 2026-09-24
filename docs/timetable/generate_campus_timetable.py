"""Writes campus-timetable.ics: the demo "Campus Timetable" for Google Calendar.

Fifteen weekly lectures across the six seeded rooms, each repeating for a semester.
Import it into the "Campus Timetable" calendar (Google Calendar -> Settings -> Import &
export -> Import, and pick that calendar), share the calendar with the service account,
and POST /api/timetable/sync mirrors every occurrence into ClassScheduleSlot.

THE LOCATION IS THE ROOM CODE, exactly — that is how the sync maps an event to a room.
The codes below are a copy of the rooms in api/Data/DbSeeder.cs: if the seed changes,
change them here and regenerate, or every event is skipped as "no matching room".

Standard library only:  python3 docs/timetable/generate_campus_timetable.py
"""

from datetime import date, datetime, timedelta, timezone
from pathlib import Path

# The Monday of the first teaching week, and how many weeks each lecture repeats.
FIRST_MONDAY = date(2026, 9, 28)
WEEKS = 16

# (room code, weekday 0=Mon, start HH:MM, end HH:MM, title) — times are Colombo local.
# MAB-101 is the busiest room on purpose: it holds the projector with the planted
# repeat-failure history, so it is the one a demo schedules maintenance into.
LECTURES = [
    ("MAB-101", 0, "08:30", "10:30", "SE3090 Software Engineering Fundamentals - Lecture"),
    ("MAB-101", 0, "13:00", "15:00", "IT3010 Network Design - Lecture"),
    ("MAB-101", 2, "10:00", "12:00", "SE3090 Software Engineering Fundamentals - Lecture"),
    ("MAB-101", 3, "14:00", "16:00", "IT3020 Database Systems - Lecture"),
    ("MAB-102", 0, "10:30", "12:30", "IT3030 Programming Applications - Lecture"),
    ("MAB-102", 2, "08:30", "10:30", "IT3040 IT Project Management - Lecture"),
    ("MAB-102", 4, "13:00", "15:00", "SE3080 Software Architecture - Lecture"),
    ("MAB-201", 1, "09:00", "11:00", "SE3090 Tutorial Group A"),
    ("MAB-201", 3, "09:00", "11:00", "SE3090 Tutorial Group B"),
    ("ENG-101", 1, "13:00", "16:00", "IT3030 Programming Applications - Lab"),
    ("ENG-101", 4, "08:30", "11:30", "IT3020 Database Systems - Lab"),
    ("ENG-102", 2, "13:00", "16:00", "SE3080 Software Architecture - Lab"),
    ("ENG-102", 3, "08:30", "11:30", "IT3010 Network Design - Lab"),
    ("ENG-301", 1, "08:30", "11:30", "EE2010 Electronics - Practical"),
    ("ENG-301", 4, "14:00", "17:00", "EE2010 Electronics - Practical"),
]

COLOMBO = timezone(timedelta(hours=5, minutes=30))


def utc_stamp(day: date, hhmm: str) -> str:
    hour, minute = map(int, hhmm.split(":"))
    local = datetime(day.year, day.month, day.day, hour, minute, tzinfo=COLOMBO)
    return local.astimezone(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def main() -> None:
    lines = [
        "BEGIN:VCALENDAR",
        "VERSION:2.0",
        "PRODID:-//MaintenX//Campus Timetable demo//EN",
        "CALSCALE:GREGORIAN",
        "X-WR-CALNAME:Campus Timetable",
        "X-WR-TIMEZONE:Asia/Colombo",
    ]

    for index, (room, weekday, start, end, title) in enumerate(LECTURES, start=1):
        first = FIRST_MONDAY + timedelta(days=weekday)
        # UTC times with a weekly rule: Colombo has no daylight saving, so a fixed UTC
        # time is the same wall-clock time every week and no VTIMEZONE block is needed.
        lines += [
            "BEGIN:VEVENT",
            f"UID:maintenx-timetable-{index:02d}@campus.test",
            f"DTSTAMP:{utc_stamp(FIRST_MONDAY, '00:00')}",
            f"DTSTART:{utc_stamp(first, start)}",
            f"DTEND:{utc_stamp(first, end)}",
            f"RRULE:FREQ=WEEKLY;COUNT={WEEKS}",
            f"SUMMARY:{title}",
            f"LOCATION:{room}",
            "END:VEVENT",
        ]

    lines.append("END:VCALENDAR")

    out = Path(__file__).with_name("campus-timetable.ics")
    # iCalendar requires CRLF line endings; .gitattributes checks .ics out as CRLF to match.
    out.write_bytes(("\r\n".join(lines) + "\r\n").encode("utf-8"))
    print(f"Wrote {len(LECTURES)} weekly lectures x {WEEKS} weeks to {out}")


if __name__ == "__main__":
    main()
