// CAL-1: the weekly calendar, drawn by FullCalendar (self-hosted, docs/decisions/0001-stack.md) from /Calendar/Events.
// The server sends clock times in the user's timezone with no offset. The calendar runs in UTC so it shows them exactly
// as given, and "now" (the today marker) is the clock time where the user is. User data is only ever set as text.
(() => {
  const element = document.getElementById('calendar');
  if (!element || !window.FullCalendar) {
    return;
  }

  const { eventsUrl, weeksUrl, createUrl, editUrl, occurrenceUrl, timeZone, firstDay, hour12, initialDate } = element.dataset;

  // CAL-4: an occurrence of a repeating activity opens its own page, to change just that one; a one-off, its form.
  const pageFor = (props) => (props.repeats
    ? `${occurrenceUrl}/${props.activityId}?date=${props.occurrenceDate}&returnToCalendar=true`
    : `${editUrl}/${props.activityId}?returnToCalendar=true`);
  const twelveHour = hour12 === 'true';
  const status = document.getElementById('calendar-status');
  const say = (text) => {
    if (status) {
      status.textContent = text;
    }
  };

  // The clock time now where the user is, as "2026-10-19T13:05:00", read by the calendar as UTC like the events.
  const clockNow = () => {
    const parts = Object.fromEntries(new Intl.DateTimeFormat('en-GB', {
      timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
    }).formatToParts(new Date()).map((part) => [part.type, part.value]));
    return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}`;
  };

  // A date the calendar works with (UTC) back to the clock time it stands for: "2026-10-19T00:00:00".
  const clockTime = (date) => date.toISOString().slice(0, 19);

  const partNames = { Preparation: 'Getting ready', TravelOut: 'Travel there', Changing: 'Changing', TravelBack: 'Travel back' };

  const load = (range, success, failure) => {
    const url = `${eventsUrl}?start=${encodeURIComponent(clockTime(range.start))}&end=${encodeURIComponent(clockTime(range.end))}`;
    fetch(url, { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`The calendar couldn't be loaded (error ${response.status}). Try again, or use the Activities page.`);
        }
        return response.json();
      })
      .then((occurrences) => {
        const events = [];
        for (const occurrence of occurrences) {
          events.push({ ...occurrence, url: pageFor(occurrence.extendedProps) });
          // CAL-5: getting ready, travel and changing, as lighter bands behind the day, so the activity keeps its full
          // width (as blocks beside it, they halved it: break-ui). Not in the list, whose "Leave home 17:35, back 19:45"
          // says the same in words.
          for (const part of occurrence.extendedProps.footprint) {
            events.push({
              id: `${occurrence.id}-${part.part}`,
              title: partNames[part.part] ?? part.part,
              start: part.start,
              end: part.end,
              display: 'background',
              classNames: ['ft-segment', ...occurrence.classNames.filter((name) => name.startsWith('cat-'))],
              extendedProps: { partOf: occurrence.title },
            });
          }
        }
        success(events);
        const count = occurrences.length;
        say(count === 0 ? 'Nothing planned on these days.' : `${count} ${count === 1 ? 'activity' : 'activities'} on these days.`);
      })
      .catch((error) => {
        failure(error);
        say(error.message);
      });
  };

  // CAL-9: which routine and week the days shown are in ("Usual week, week B"), from the server, which works it out.
  // Nothing for someone with no routines; if it can't be loaded, the line stays empty rather than saying something wrong.
  // Only the latest answer is shown: moving quickly through the weeks mustn't leave an earlier week's words.
  const weeksLine = document.getElementById('calendar-weeks');
  let weeksAsked = 0;
  const showWeeks = (range) => {
    if (!weeksLine) {
      return;
    }
    weeksLine.textContent = '';
    const asked = ++weeksAsked;
    const url = `${weeksUrl}?start=${encodeURIComponent(clockTime(range.start))}&end=${encodeURIComponent(clockTime(range.end))}`;
    fetch(url, { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
      .then((response) => (response.ok ? response.json() : { text: null }))
      .then((weeks) => {
        if (asked === weeksAsked) {
          weeksLine.textContent = weeks.text ? `Routine: ${weeks.text}` : '';
        }
      })
      .catch(() => {
        if (asked === weeksAsked) {
          weeksLine.textContent = '';
        }
      });
  };

  const line = (text, className) => {
    const node = document.createElement('div');
    node.className = className;
    node.textContent = text;
    return node;
  };

  // What each block says: built as text nodes, never HTML, because titles and notes are the user's own words.
  const content = (info) => {
    const props = info.event.extendedProps;
    if (props.partOf) {
      const node = line(info.event.title, 'ft-event-title');
      const whose = document.createElement('span');
      whose.className = 'visually-hidden';
      whose.textContent = ` for ${props.partOf}`;
      node.append(whose);
      return { domNodes: [node] };
    }

    const inList = info.view.type.startsWith('list');
    const nodes = [];
    // In the grid: the times the server wrote, the user's way ("01:30 BST" on the night the clocks go back, "06:00 the
    // next day" for a night shift). A short block shows its start and title; its end is still read out (site.css).
    if (!inList) {
      const time = line(info.isStart ? props.starts : `Until ${props.endsAt}`, 'ft-event-time');
      if (info.isStart) {
        const end = document.createElement('span');
        end.className = 'ft-event-end';
        end.textContent = ` – ${props.ends}`;
        time.append(end);
      }
      nodes.push(time);
    }
    // In the list the row isn't a link once its content is replaced, so the title is one: the keyboard and screen
    // readers can open it too (break-ui). In the grid the whole block is already a link.
    if (inList) {
      const title = line('', 'ft-event-title');
      const link = document.createElement('a');
      link.href = pageFor(props);
      link.textContent = info.event.title;
      title.append(link);
      nodes.push(title);
    } else {
      nodes.push(line(info.event.title, 'ft-event-title'));
    }
    if (props.moved) {
      nodes.push(line('Moved, this time only', 'ft-event-details'));
    }
    if (inList) {
      const details = [props.category, props.place ? `at ${props.place}` : null, props.trip].filter(Boolean).join(' · ');
      if (details) {
        nodes.push(line(details, 'ft-event-details'));
      }
      if (props.note) {
        nodes.push(line(props.note, 'ft-event-details'));
      }
    }
    // CAL-6: a clash in words and with an icon, not by colour alone. The grid says how many (the names didn't fit); the
    // list and the activity's page name them.
    const clashes = props.clashesWith.length;
    if (clashes > 0) {
      nodes.push(line(
        inList ? `⚠ Clashes with ${props.clashesWith.join(', ')}` : `⚠ Clashes with ${clashes === 1 ? '1 other' : `${clashes} others`}`,
        'ft-event-clash',
      ));
    }
    return { domNodes: nodes };
  };

  // The list's own time column, in the server's words too: "22:00 – 06:00 the next day", not "22:00 – 00:00", and
  // "Until 06:00" for the part after midnight.
  const listTimes = (info) => {
    const props = info.event.extendedProps;
    const cell = info.el.querySelector('.fc-list-event-time');
    if (cell && props.starts) {
      cell.textContent = info.isStart ? `${props.starts} – ${props.ends}` : `Until ${props.endsAt}`;
    }
  };

  // CAL-8: a drag or a resize is never saved by itself. The block goes back, and the occurrence's page (or a one-off's
  // form) opens with the new time and length filled in, to check (SCH-1) and confirm: the same page a keyboard reaches.
  // The length is the real, elapsed one (§8), plus what a resize added: on the nights the clocks change, the clock times
  // drawn differ from it by an hour.
  const dragged = (info) => {
    const props = info.event.extendedProps;
    const start = clockTime(info.event.start).slice(0, 16);
    const added = info.endDelta ? (info.endDelta.days * 1440) + Math.round(info.endDelta.milliseconds / 60000) : 0;
    const minutes = props.minutes + added;
    info.revert();
    const page = pageFor(props);
    window.location.assign(`${page}&start=${encodeURIComponent(start)}&minutes=${minutes}`);
  };

  // CAL-10: on a phone the list is easier to read than a week of narrow columns.
  const narrow = window.matchMedia('(max-width: 767.98px)').matches;
  // Times as every other page writes them (ACC-3): "18:30", or "6:30pm" with the 12-hour clock.
  const pad = (number) => String(number).padStart(2, '0');
  const time = ({ date }) => (twelveHour
    ? `${date.hour % 12 || 12}:${pad(date.minute)}${date.hour < 12 ? 'am' : 'pm'}`
    : `${pad(date.hour)}:${pad(date.minute)}`);

  const calendar = new FullCalendar.Calendar(element, {
    timeZone: 'UTC',
    now: clockNow,
    // British date order ("Mon 19 Oct"); FullCalendar only needs the code, as the formats come from the browser.
    locales: [{
      code: 'en-gb',
      week: { dow: 1, doy: 4 },
      buttonText: { prev: 'Previous', next: 'Next', today: 'Today', week: 'Week', day: 'Day', list: 'List' },
      allDayText: 'All day',
      noEventsText: 'Nothing planned on these days.',
    }],
    locale: 'en-gb',
    firstDay: Number(firstDay),
    initialView: narrow ? 'listWeek' : 'timeGridWeek',
    // Back from adding or changing an activity: its week. Empty: today's.
    initialDate: initialDate || undefined,
    // An empty time opens "Add an activity" there (the button above the calendar does the same from the keyboard).
    dateClick: (info) => {
      const at = clockTime(info.date);
      window.location.assign(`${createUrl}?date=${at.slice(0, 10)}&time=${at.slice(11, 16)}&returnToCalendar=true`);
    },
    headerToolbar: { left: 'prev,next today', center: 'title', right: 'timeGridWeek,timeGridDay,listWeek' },
    // Words, not FullCalendar's icon font: the font is a data: URL, which the Content-Security-Policy blocks, and
    // words say more to everyone.
    buttonIcons: false,
    allDaySlot: false,
    nowIndicator: true,
    height: 'auto',
    slotLabelFormat: time,
    eventTimeFormat: time,
    // "Mon 19 Oct", as on every other page.
    dayHeaderFormat: { weekday: 'short', day: 'numeric', month: 'short' },
    // WCAG 2.5.8: even a ten-minute activity is a target at least 24 pixels tall (FullCalendar takes 1 off for the gap).
    eventMinHeight: 25,
    // Two side by side, then "+3 more", which opens the rest: six overlapping activities in a column were unreadable
    // at any width (break-ui). Clashes are rare, and each says so in words.
    eventMaxStack: 2,
    events: load,
    datesSet: showWeeks,
    eventContent: content,
    eventDidMount: listTimes,
    editable: true,
    eventDrop: dragged,
    eventResize: dragged,
  });
  calendar.render();
})();
