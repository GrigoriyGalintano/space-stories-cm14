# Requisition Computer
requisition-paperwork-receiver-name = Отдел логистики
requisition-paperwork-reward-message = Подтверждение получено! Переведено ${ $amount } из излишков бюджета.

# Stories-BlackMarket-Start
rmc-requisitions-category-ammo = Боеприпасы
rmc-requisitions-category-attachments = Обвесы
rmc-requisitions-category-clothing = Одежда
rmc-requisitions-category-engineering = Инженерия
rmc-requisitions-category-explosives = Взрывчатка
rmc-requisitions-category-food = Еда
rmc-requisitions-category-gear = Снаряжение
rmc-requisitions-category-medical = Медицина
rmc-requisitions-category-mortar = Мортира
rmc-requisitions-category-reagent-tanks = Резервуары для реагентов
rmc-requisitions-category-research = Исследование
rmc-requisitions-category-restricted-equipment = Ограниченное оборудование
rmc-requisitions-category-vehicle-ammo = Боеприпасы для техники
rmc-requisitions-category-weapons = Оружие
rmc-requisitions-category-weapons-specialist-ammo = Боеприпасы специалиста по оружию
# Stories-BlackMarket-End

# Requisition Invoice
requisition-paper-print-name = { $name } счет-фактура
requisition-paper-print-manifest = { "[" }head=2]
    { $containerName }[/head][bold]{ $content }[/bold][head=2]
    Вес: { $weight } фунтов
    Партия: { $lot }
    Серийный номер: { $serialNumber }[/head]
requisition-paper-print-content = - { $count } { $item }

# Supply Drop Console
ui-supply-drop-consle-name = Консоль вызова припасов
ui-supply-drop-console-name-bolded = [bold]ВЫЗОВ ПРИПАСОВ[/bold]
ui-supply-drop-console-longitude = Долгота:
ui-supply-drop-console-latitude = Широта:
ui-supply-drop-pad-status = [bold]Состояние площадки снабжения[/bold]
ui-supply-drop-console-update = Обновить
ui-supply-drop-console-ready = Готово к запуску!
ui-supply-drop-console-launch = ЗАПУСТИТЬ ВЫЗОВ ПРИПАСОВ
ui-supply-drop-console-launch-confirmation = Вы уверены, что хотите отправить ящик?
ui-supply-drop-console-cooldown = { $time } секунд до следующего запуска
ui-supply-drop-crate-status =
    { $hasCrate ->
        [true] Состояние площадки снабжения: контейнер загружен.
        *[false] Контейнер не загружен.
    }
