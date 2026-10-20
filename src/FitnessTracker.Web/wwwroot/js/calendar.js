// CAL-1: the weekly calendar, drawn by FullCalendar (self-hosted, docs/decisions/0001-stack.md) from /Calendar/Events.
// The server sends clock times in the user's timezone with no offset. The calendar runs in UTC so it shows them exactly
// as given, and "now" (the today marker) is the clock time where the user is. User data is only ever set as text.
(() => {
  const element = document.getElementById('calendar');
  if (!element || !window.FullCalendar) {
    return;
  }

  const { eventsUrl, timeZone, firstDay, hour12 } = element.dataset;
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
          events.push({ ...occurrence, url: `/Activities/Edit/${occurrence.extendedProps.activityId}` });
          // CAL-5: getting ready, travel and changing, as lighter blocks joined to the activity (not in the list).
          for (const part of occurrence.extendedProps.footprint) {
            events.push({
              id: `${occurrence.id}-${part.part}`,
              title: partNames[part.part] ?? part.part,
              start: part.start,
              end: part.end,
              classNames: ['ft-segment', ...occurrence.classNames.filter((name) => name.startsWith('cat-'))],
              // Not a link and not a tab stop: there's nothing to do with it on its own.
              interactive: false,
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
    // The start with its zone ("01:30 BST") when the clocks repeat that time; otherwise the calendar's own text.
    const when = props.starts.includes(' ') ? props.starts : info.timeText;
    if (!inList && when) {
      nodes.push(line(when, 'ft-event-time'));
    }
    nodes.push(line(info.event.title, 'ft-event-title'));
    if (inList) {
      const details = [props.category, props.place ? `at ${props.place}` : null, props.trip].filter(Boolean).join(' · ');
      if (details) {
        nodes.push(line(details, 'ft-event-details'));
      }
      if (props.note) {
        nodes.push(line(props.note, 'ft-event-details'));
      }
    }
    // CAL-6: a clash in words and with an icon, not by colour alone.
    if (props.clashesWith.length > 0) {
      nodes.push(line(`⚠ Clashes with ${props.clashesWith.join(', ')}`, 'ft-event-clash'));
    }
    return { domNodes: nodes };
  };

  // CAL-10: on a phone the list is easier to read than a week of narrow columns.
  const narrow = window.matchMedia('(max-width: 767.98px)').matches;
  const time = { hour: '2-digit', minute: '2-digit', hour12: twelveHour };

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
    headerToolbar: { left: 'prev,next today', center: 'title', right: 'timeGridWeek,timeGridDay,listWeek' },
    // Words, not FullCalendar's icon font: the font is a data: URL, which the Content-Security-Policy blocks, and
    // words say more to everyone.
    buttonIcons: false,
    allDaySlot: false,
    nowIndicator: true,
    height: 'auto',
    slotLabelFormat: time,
    eventTimeFormat: time,
    events: load,
    eventContent: content,
  });
  calendar.render();
})();
