using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FiguresDotStore.Controllers
{
    // Здесь всё хорошо, кроме названия интерфейса.
    // Сегодня это Redis, а завтра захотим использовать Memcached или еще что-нибудь.
    // Лучше бы просто назвать ICacheClient, например.
    internal interface IRedisClient
    {
        int Get(string type);

        void Set(string type, int current);
    }

    // Использование этого класса в FiguresController'е затрудняет написание unit тестов на FiguresController.
    // Помимо тестирования методов FiguresController'а мы будем тестировать и реальные методы FiguresStorage'а.
    // Лучше сделать интерфейс IFiguresStorage и инжектить его в FiguresController как зависимость,
    // а для тестов настроить заглушку-mock.
    // Если нужен именно singleton, это тоже легко настраивается через упарвление зависимостями.
    // Также непонятно, что здесь с потокобезопасностью, т.к. на каждый запрос будет создаваться свой
    // FiguresController, а вот FiguresStorage будет общий для всех.
    // В данных методах проблем с потокобезопасностью нет (не используются статические переменные, например),
    // все упирается в потокобезопасность RedisClient'а.
    public static class FiguresStorage
    {
        // корректно сконфигурированный и готовый к использованию клиент Редиса
        private static IRedisClient RedisClient { get; }

        public static bool CheckIfAvailable(string type, int count)
        {
            // Нужно проверять входные аргументы у публичных методов.
            // В данном случае нужно проверить, что count > 0.
            return RedisClient.Get(type) >= count;
        }

        public static void Reserve(string type, int count)
        {
            var current = RedisClient.Get(type);

            // Где проверка, что current > count?
            // И если не удалось зарезервировать, должно выбрасываться специальное исключение.
            RedisClient.Set(type, current - count);
        }
    }

    public class Position
    {
        // Этот "код с запашком" (code smells) называется cтроковая типизация (stringly typed).
        // Если в каком-то месте опечататься, то ошибку потом будет очень сложно найти.
        // Это "зло" нужно поменять на перечисление (enum) например.
        public string Type { get; set; }

        // float - тип данных с низкой точностью.
        // Лучше использовать тип данных с фиксированной запятой (decimal) или двойной точности (double),
        // т.к. float дает большие погрешности в рассчетах.
        // И немного непонятно, зачем хранить в позиции все три габарита - длину, ширину и высоту,
        // когда транспортные компании интересуют только вес и объем, но это уже вопрос не к коду, а к архитектуре.
        public float SideA { get; set; }
        public float SideB { get; set; }
        public float SideC { get; set; }

        // Для каждого свойства нужно использовать наиболее подходящий тип данных.
        // Сам тип данных должен накладывать ограничения на поле, если они есть в реальном объекте.
        // В данном случае количество не может быть отрицательным числом - это не имеет смысла,
        // лучше использовать тип uint.
        public int Count { get; set; }
    }

    public class Cart
    {
        // Здесь, потенциально, список Positions может быть непроинициализирован (иметь значение null).
        // Нужно либо добавить инициализацию в конструктор, либо добавить конструктор с параметром, чтобы
        // свойство Positions всегда было проинициализировано.
        public List<Position> Positions { get; set; }
    }

    public class Order
    {
        public List<Figure> Positions { get; set; }

        // Заказ не должен уметь сам себя обсчитывать, это нарушение принципа единственности ответственности (SRP)
        // из SOLID принципов. Нужно создать отдельный класс калькулятор, который будет обсчитывать, то что нужно -
        // общую сумму заказа, если нужно рассчитать скидку (TotalSumCalculator например)
        // или общий объем и вес, если нужно рассчитать доставку (TotalVolumeAndWeightCalculator например).
        public decimal GetTotal() =>
            Positions.Select(p => p switch
				{

                    Triangle => (decimal)p.GetArea() * 1.2m,
                    Circle => (decimal)p.GetArea() * 0.9m

                })
				.Sum();
}

public abstract class Figure
{
    public float SideA { get; set; }
    public float SideB { get; set; }
    public float SideC { get; set; }

    // В зависимости от задачи, валидацию тоже можно вынести в отдельный класс для разделения ответственности.
    // Но если нужно валидировать "на место", то лучше валидировать значения сразу в setter'ах свойств,
    // если возможно или добавить конструктор с параметрами и валидировать значения в конструкторе.
    public abstract void Validate();

    // Расчет площади фигуры тоже можно доверить внешнему калькулятору.
    // Чтобы создать калькулятор, который рассчитает площадь для соответствующей фигуры, нужно использовать
    // шаблон "Фабрика". Фабрика будет возвращать подходящий калькулятор в зависимости от типа фигуры.
    public abstract double GetArea();
}

public class Triangle : Figure
{
    public override void Validate()
    {
        bool CheckTriangleInequality(float a, float b, float c) => a < b + c;

        // Можно немного улучшить код, инвертировав if.
        // Из-за && будут проверяться все условия, хотя несоблюдения хотя бы одного из них достаточно,
        // чтобы выбросить исключение.
        if (CheckTriangleInequality(SideA, SideB, SideC)
            && CheckTriangleInequality(SideB, SideA, SideC)
            && CheckTriangleInequality(SideC, SideB, SideA))
            return;

        // Нет проверки на то, что длины сторон положительные.

        // Лучше создать свой тип исключения, унаследовавшись от подходящего исключения,
        // дабы не путать "свои" исключения с системными и обрабатывать "свои" исключения особым образом.
        throw new InvalidOperationException("Triangle restrictions not met");
    }

    public override double GetArea()
    {
        var p = (SideA + SideB + SideC) / 2;
        return Math.Sqrt(p * (p - SideA) * (p - SideB) * (p - SideC));
    }

}

// Здесь завуалированный классический вопрос по ООП про прямоугольник и квадрат на нарушение
// принципа подстановки Барбары Лисков (LSP). Фигура квадрат накладывает ограничения на соотношение сторон
// (стороны должны быть равны), хотя в исходном классе таких ограничений нет.
public class Square : Figure
{
    public override void Validate()
    {
        if (SideA < 0)
            throw new InvalidOperationException("Square restrictions not met");

        if (SideA != SideB) // Значения типа float так сравнивать нельзя из-за их низкой точности.
            throw new InvalidOperationException("Square restrictions not met");
    }

    public override double GetArea() => SideA * SideA;
}

public class Circle : Figure
{
    public override void Validate()
    {
        if (SideA < 0)
            throw new InvalidOperationException("Circle restrictions not met");
    }

    public override double GetArea() => Math.PI * SideA * SideA;
}

public interface IOrderStorage
{
    // сохраняет оформленный заказ и возвращает сумму
    // Название асинхронного метода желательно заканчивать постфиксом Async, чтобы отличать от синхронных.
    // Ну и давать возможность отменить выполнение операции - добавить параметр CancellationToken.
    Task<decimal> Save(Order order);
}

[ApiController]
[Route("[controller]")]
public class FiguresController : ControllerBase
{
    // Не используется в коде, можно удалить лишнюю зависимость.
    private readonly ILogger<FiguresController> _logger;

    private readonly IOrderStorage _orderStorage;

    public FiguresController(ILogger<FiguresController> logger, IOrderStorage orderStorage)
    {
        _logger = logger;
        _orderStorage = orderStorage;
    }

    // хотим оформить заказ и получить в ответе его стоимость
    [HttpPost]
    public async Task<ActionResult> Order(Cart cart)
    {
        // Где гарантия того, что за время между проверкой наличия товара на складе и его резервированием,
        // кто-нибудь его не купит? Т.е. на момент резервирования товара уже может не быть на складе.
        // Такие оперции нужно проводить в рамках транзакции. В данном случае, поскольку здесь не SQL база данных,
        // необходима т.н. бизнес транзакция, вроде даже паттерн такой есть у Мартина Фаулера в книжке
        // "Шаблоны корпоративных приложений".
        foreach (var position in cart.Positions) // Можно использовать Parallel.ForEach, т.к. нет зависимости по данным.
        {
            if (!FiguresStorage.CheckIfAvailable(position.Type, position.Count))
            {
                return new BadRequestResult();
            }
        }

        // Нужно избегать создания экземпляра класса явно через new, лучше создать OrderFactory, например, и создавать в ней.
        var order = new Order
        {
            Positions = cart.Positions.Select(p =>
            {
                // Создавать фигуру тоже должна фабрика.
                Figure figure = p.Type switch

                {

                    "Circle" => new Circle(),
						"Triangle" => new Triangle(),
						"Square" => new Square()

                };
        // Здесь напрашивается автомаппер.
        figure.SideA = p.SideA;
        figure.SideB = p.SideB;
        figure.SideC = p.SideC;
        figure.Validate();
        return figure;
    }).ToList()

            };

			foreach (var position in cart.Positions)
			{
				FiguresStorage.Reserve(position.Type, position.Count);
                // Здесь должно обрабатываться исключение, которое выбрасывается в случае, если не удалось
                // зарезервиовать.
			}

            // Здесь должен быть await.
            // Обращение к свойству Result Task'а блокирует вызывающий поток (блокирующее ожидание),
            // что сводит на нет все преимущества асинхронного программирования.
			var result = _orderStorage.Save(order);

			return new OkObjectResult(result.Result);
		}
	}
}