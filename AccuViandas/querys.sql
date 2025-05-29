SELECT
    DM.Id AS MenuId,
    DM.Date,
    DMI.Id AS MenuItemId,
    DMI.Name,
    DMI.Category
FROM
    DailyMenus AS DM
JOIN
    DailyMenuItems AS DMI ON DM.Id = DMI.DailyMenuId
WHERE
    DM.Date = '2025-05-27 00:00:00'; -- Usa la fecha exacta que ingresaste, incluyendo la hora 00:00:00 si la API la guarda así