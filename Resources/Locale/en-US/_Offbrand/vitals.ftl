-crew-monitor-vitals-rating = { $rating ->
    [good] [color=#00D3B8]⚫ {$text}[/color]
    [okay] [color=#30CC19]◕ {$text}[/color]
    [poor] [color=#bdcc00]◑ {$text}[/color]
    [bad] [color=#E8CB2D]◔ {$text}[/color]
    [awful] [color=#EF973C]○ {$text}[/color]
    [dangerous] [color=#FF6C7F]◌ {$text}[/color]
   *[other] unknown ?
    }
-crew-monitor-vitals-damage-rating = { $rating ->
    [good] [color=#00D3B8]███[/color]
    [okay] [color=#30CC19]██▓[/color]
    [poor] [color=#bdcc00]█▓▒[/color]
    [bad] [color=#E8CB2D]▓▒░[/color]
    [awful] [color=#EF973C]▒░░[/color]
    [dangerous] [color=#FF6C7F]░░░[/color]
   *[other] unknown ?
    }

offbrand-crew-monitoring-damage-estimate = { -crew-monitor-vitals-damage-rating(rating: $rating) }
offbrand-crew-monitoring-heart-rate = { -crew-monitor-vitals-rating(text: $rate, rating: $rating) }bpm
offbrand-crew-monitoring-blood-pressure = {$systolic}/{$diastolic}
offbrand-crew-monitoring-spo2 = { -crew-monitor-vitals-rating(text: $value, rating: $rating) }% {LOC($spo2)}
