# Выбор и лимиты (Fluent-логика)

-markings-selection = { $selectable ->
    [0] Больше нельзя выбрать черты.
    [one] Можно выбрать ещё одну черту.
   *[other] Можно выбрать ещё { $selectable } черт.
}

markings-limits = { $required ->
    [true] { $count ->
        [-1] Выберите хотя бы одну черту.
        [0] Нельзя выбрать ни одной черты, но как-то нужно? Это баг.
        [one] Выберите одну черту.
       *[other] Выберите как минимум одну и до {$count} черт. { -markings-selection(selectable: $selectable) }
    }
   *[false] { $count ->
        [-1] Выберите любое количество черт.
        [0] Нельзя выбрать ни одной черты.
        [one] Выберите до одной черты.
       *[other] Выберите до {$count} черт. { -markings-selection(selectable: $selectable) }
    }
}

# Интерфейс и базовые действия

markings-search = Поиск
markings-used = Используемые черты
markings-unused = Неиспользуемые черты
markings-add = Добавить черту
markings-remove = Убрать черту
markings-reorder = Изменить порядок черт
markings-rank-up = Вверх
markings-rank-down = Вниз
marking-points-remaining = Черт осталось: { $points }
marking-used = { $marking-name }
marking-used-forced = { $marking-name } (Принудительно)
marking-slot-add = Добавить
marking-slot-remove = Удалить
marking-slot = Слот { $number }

# Модификаторы

humanoid-marking-modifier-enable = Включить
humanoid-marking-modifier-prototype-id = ID прототипа:
humanoid-marking-modifier-base-layers = Базовый слой
humanoid-marking-modifier-respect-limits = Учитывать лимиты
humanoid-marking-modifier-respect-group-sex = Учитывать ограничения по полу и группе
humanoid-marking-modifier-force = Принудительно
humanoid-marking-modifier-ignore-species = Игнорировать вид

# Части тела (Органы)

markings-organ-Head = Голова
markings-organ-Torso = Туловище
markings-organ-ArmLeft = Левая рука
markings-organ-ArmRight = Правая рука
markings-organ-HandLeft = Левая кисть
markings-organ-HandRight = Правая кисть
markings-organ-LegLeft = Левая нога
markings-organ-LegRight = Правая нога
markings-organ-FootLeft = Левая стопа
markings-organ-FootRight = Правая стопа
markings-organ-Eyes = Глаза

# Слои (Layers)

markings-layer-Special = Специальное
markings-layer-Tail = Хвост
markings-layer-Tail-Moth = Крылья
markings-layer-Hair = Причёска
markings-layer-FacialHair = Лицевая растительность
markings-layer-UndergarmentTop = Нижнее бельё (Верх)
markings-layer-UndergarmentBottom = Нижнее бельё (Низ)
markings-layer-Chest = Грудь
markings-layer-Head = Голова
markings-layer-Snout = Морда
markings-layer-SnoutCover = Морда (Внешний)
markings-layer-HeadSide = Голова (бок)
markings-layer-HeadTop = Голова (верх)
markings-layer-Eyes = Глаза
markings-layer-RArm = Правая рука
markings-layer-LArm = Левая рука
markings-layer-RHand = Правая кисть
markings-layer-LHand = Левая кисть
markings-layer-RLeg = Правая нога
markings-layer-LLeg = Левая нога
markings-layer-RFoot = Правая стопа
markings-layer-LFoot = Левая стопа
markings-layer-Overlay = Наложение
markings-layer-TailOverlay = Наложение хвоста

# Категории (Categories)

markings-category-Special = Специальное
markings-category-Hair = Причёска
markings-category-FacialHair = Лицевая растительность
markings-category-Head = Голова
markings-category-HeadTop = Голова (верх)
markings-category-HeadSide = Голова (бок)
markings-category-Snout = Морда
markings-category-SnoutCover = Морда (внешний)
markings-category-UndergarmentTop = Нижнее бельё (верх)
markings-category-UndergarmentBottom = Нижнее бельё (низ)
markings-category-Chest = Грудь
markings-category-Arms = Руки
markings-category-Legs = Ноги
markings-category-Tail = Хвост
markings-category-Overlay = Наложение
