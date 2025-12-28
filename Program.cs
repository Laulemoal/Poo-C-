using System;
using System.Collections.Generic;
using System.Linq;

namespace GymManagerLite
{
    // =========================
    // 1) ABSTRACCIÓN + HERENCIA
    // =========================
    public abstract class Person
    {
        public Guid Id { get; } = Guid.NewGuid();

        private string _name;
        public string Name
        {
            get => _name;
            private set => _name = string.IsNullOrWhiteSpace(value)
                ? throw new DomainException("El nombre no puede estar vacío.")
                : value.Trim();
        }

        protected Person(string name)
        {
            Name = name;
        }

        // Polimorfismo (método virtual/override)
        public virtual string GetRoleDescription() => "Persona";
    }

    // sealed para mostrar el concepto
    public sealed class Member : Person
    {
        // =========================
        // 2) ENCAPSULAMIENTO
        // =========================
        public DateTime JoinDate { get; } = DateTime.Now;

        // =========================
        // 3) COMPOSICIÓN (Member TIENE un Plan, y lo controla)
        // =========================
        public Plan Plan { get; private set; }

        public bool IsActive { get; private set; } = true;

        // Agregación: el Member no "posee" los pagos; se registran en otro lado,
        // pero acá guardamos referencias por comodidad.
        private readonly List<Payment> _payments = new();
        public IReadOnlyList<Payment> Payments => _payments.AsReadOnly();

        public Member(string name, Plan plan) : base(name)
        {
            Plan = plan ?? throw new DomainException("Plan inválido.");
        }

        public void ChangePlan(Plan newPlan)
        {
            Plan = newPlan ?? throw new DomainException("Nuevo plan inválido.");
        }

        public void Deactivate() => IsActive = false;

        internal void AddPayment(Payment payment) => _payments.Add(payment);

        public override string GetRoleDescription() => "Socio";
    }

    public class Staff : Person
    {
        public string Position { get; }

        public Staff(string name, string position) : base(name)
        {
            Position = string.IsNullOrWhiteSpace(position) ? "Staff" : position.Trim();
        }

        public override string GetRoleDescription() => $"Staff ({Position})";
    }

    // =========================
    // 4) CLASE DE DOMINIO SIMPLE
    // =========================
    public class Plan
    {
        public string Name { get; }
        public decimal BaseMonthlyFee { get; }

        public Plan(string name, decimal baseMonthlyFee)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Nombre de plan inválido.");
            if (baseMonthlyFee <= 0) throw new DomainException("La cuota base debe ser > 0.");

            Name = name.Trim();
            BaseMonthlyFee = baseMonthlyFee;
        }

        public override string ToString() => $"{Name} (${BaseMonthlyFee})";
    }

    public class Payment
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid MemberId { get; }
        public decimal Amount { get; }
        public DateTime Date { get; } = DateTime.Now;

        public Payment(Guid memberId, decimal amount)
        {
            if (amount <= 0) throw new DomainException("El pago debe ser > 0.");
            MemberId = memberId;
            Amount = amount;
        }

        public override string ToString() => $"{Date:yyyy-MM-dd HH:mm} - ${Amount}";
    }

    // =========================
    // 5) INTERFACES + GENÉRICOS
    // =========================
    public interface IRepository<T>
    {
        void Add(T item);
        T Get(Guid id);
        IEnumerable<T> GetAll();
        void Remove(Guid id);
    }

    public class InMemoryRepository<T> : IRepository<T> where T : class
    {
        // Guarda objetos por Id (usamos reflexión simple para este mini-proyecto)
        private readonly Dictionary<Guid, T> _items = new();

        private static Guid GetId(T item)
        {
            var prop = typeof(T).GetProperty("Id");
            if (prop == null) throw new DomainException($"Tipo {typeof(T).Name} no tiene propiedad Id.");
            return (Guid)(prop.GetValue(item) ?? Guid.Empty);
        }

        public void Add(T item)
        {
            if (item == null) throw new DomainException("Item null.");
            var id = GetId(item);
            if (id == Guid.Empty) throw new DomainException("Id inválido.");
            if (_items.ContainsKey(id)) throw new DomainException("Item duplicado.");
            _items[id] = item;
        }

        public T Get(Guid id)
        {
            if (!_items.TryGetValue(id, out var item))
                throw new NotFoundException($"No existe {typeof(T).Name} con Id={id}.");
            return item;
        }

        public IEnumerable<T> GetAll() => _items.Values.ToList();

        public void Remove(Guid id)
        {
            if (!_items.Remove(id))
                throw new NotFoundException($"No existe {typeof(T).Name} con Id={id}.");
        }
    }

    // =========================
    // 6) DI SIMPLE + POLIMORFISMO (estrategia)
    // =========================
    public interface IPricingPolicy
    {
        decimal GetMonthlyFee(Member member);
    }

    public class StandardPricingPolicy : IPricingPolicy
    {
        public decimal GetMonthlyFee(Member member)
        {
            // ejemplo: si no está activo, no cobra
            if (!member.IsActive) return 0;
            return member.Plan.BaseMonthlyFee;
        }
    }

    public class Promo10PercentPolicy : IPricingPolicy
    {
        public decimal GetMonthlyFee(Member member)
        {
            if (!member.IsActive) return 0;
            return member.Plan.BaseMonthlyFee * 0.90m;
        }
    }

    // =========================
    // 7) EVENTOS
    // =========================
    public class PaymentService
    {
        private readonly IRepository<Payment> _paymentsRepo;

        public event EventHandler<PaymentRegisteredEventArgs>? PaymentRegistered;

        public PaymentService(IRepository<Payment> paymentsRepo)
        {
            _paymentsRepo = paymentsRepo;
        }

        public void RegisterPayment(Member member, decimal amount)
        {
            if (member == null) throw new DomainException("Member null.");
            if (!member.IsActive) throw new DomainException("El socio está inactivo.");

            var payment = new Payment(member.Id, amount);
            _paymentsRepo.Add(payment);

            // Agregación: asociamos
            member.AddPayment(payment);

            PaymentRegistered?.Invoke(this, new PaymentRegisteredEventArgs(member, payment));
        }
    }

    public class PaymentRegisteredEventArgs : EventArgs
    {
        public Member Member { get; }
        public Payment Payment { get; }

        public PaymentRegisteredEventArgs(Member member, Payment payment)
        {
            Member = member;
            Payment = payment;
        }
    }

    // =========================
    // 8) SERVICIO PRINCIPAL (orquesta)
    // =========================
    public class GymService
    {
        private readonly IRepository<Member> _membersRepo;
        private readonly PaymentService _paymentService;
        private readonly IPricingPolicy _pricing;

        public GymService(IRepository<Member> membersRepo, PaymentService paymentService, IPricingPolicy pricing)
        {
            _membersRepo = membersRepo;
            _paymentService = paymentService;
            _pricing = pricing;
        }

        public Member EnrollMember(string name, Plan plan)
        {
            var m = new Member(name, plan);
            _membersRepo.Add(m);
            return m;
        }

        public decimal GetMonthlyFee(Guid memberId)
        {
            var member = _membersRepo.Get(memberId);
            return _pricing.GetMonthlyFee(member);
        }

        public void Pay(Guid memberId, decimal amount)
        {
            var member = _membersRepo.Get(memberId);
            _paymentService.RegisterPayment(member, amount);
        }

        public IEnumerable<Member> ListMembers() => _membersRepo.GetAll();
    }

    // =========================
    // 9) EXCEPCIONES CUSTOM
    // =========================
    public class DomainException : Exception
    {
        public DomainException(string message) : base(message) { }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    // =========================
    // 10) STATIC (utilidades)
    // =========================
    public static class Utils
    {
        public static void Title(string text)
        {
            Console.WriteLine("\n" + new string('=', text.Length));
            Console.WriteLine(text);
            Console.WriteLine(new string('=', text.Length));
        }
    }

    // =========================
    // APP
    // =========================
    internal class Program
    {

        static void Main()
        {
            IRepository<Member> memberRepo = new InMemoryRepository<Member>();
            IRepository<Payment> paymentRepo = new InMemoryRepository<Payment>();

            var paymentService = new PaymentService(paymentRepo);
            var pricing = new StandardPricingPolicy();
            var gym = new GymService(memberRepo, paymentService, pricing);

            var basic = new Plan("Basic", 15000);
            var full = new Plan("Full", 25000);

            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== GYM MANAGER LITE ===");
                Console.WriteLine("1 - Crear socio");
                Console.WriteLine("2 - Listar socios");
                Console.WriteLine("3 - Actualizar socio");
                Console.WriteLine("4 - Eliminar socio");
                Console.WriteLine("5 - Salir");
                Console.Write("Opción: ");

                var option = Console.ReadLine();

                try
                {
                    switch (option)
                    {
                        case "1":
                            CreateMember(gym, basic, full);
                            break;

                        case "2":
                            ListMembers(gym);
                            break;

                        case "3":
                            UpdateMember(gym, basic, full);
                            break;

                        case "4":
                            DeleteMember(gym);
                            break;

                        case "5":
                            return;

                        default:
                            Console.WriteLine("Opción inválida");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                Console.WriteLine("\nPresione ENTER para continuar...");
                Console.ReadLine();
            }
        }

        static void CreateMember(GymService gym, Plan basic, Plan full)
        {
            Console.Write("Nombre del socio: ");
            var name = Console.ReadLine();

            Console.WriteLine("Plan: 1-Basic | 2-Full");
            var planOption = Console.ReadLine();

            var plan = planOption == "2" ? full : basic;

            var member = gym.EnrollMember(name, plan);

            Console.WriteLine($"Socio creado con ID: {member.Id}");
        }

        static void ListMembers(GymService gym)
        {
            var members = gym.ListMembers();

            Console.WriteLine("\n--- SOCIOS ---");
            foreach (var m in members)
            {
                Console.WriteLine($"ID: {m.Id}");
                Console.WriteLine($"Nombre: {m.Name}");
                Console.WriteLine($"Plan: {m.Plan.Name}");
                Console.WriteLine($"Activo: {m.IsActive}");
                Console.WriteLine("-------------------");
            }
        }

        static void UpdateMember(GymService gym, Plan basic, Plan full)
        {
            Console.Write("ID del socio: ");
            var id = Guid.Parse(Console.ReadLine());

            Console.WriteLine("1 - Cambiar plan");
            Console.WriteLine("2 - Dar de baja");
            var option = Console.ReadLine();

            var member = gym.ListMembers().First(m => m.Id == id);

            if (option == "1")
            {
                Console.WriteLine("Nuevo plan: 1-Basic | 2-Full");
                var planOption = Console.ReadLine();
                member.ChangePlan(planOption == "2" ? full : basic);
                Console.WriteLine("Plan actualizado");
            }
            else if (option == "2")
            {
                member.Deactivate();
                Console.WriteLine("Socio dado de baja");
            }
        }

        static void DeleteMember(GymService gym)
        {
            Console.Write("ID del socio a eliminar: ");
            var id = Guid.Parse(Console.ReadLine());

            var repoField = typeof(GymService)
                .GetField("_membersRepo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var repo = (IRepository<Member>)repoField.GetValue(gym);
            repo.Remove(id);

            Console.WriteLine("Socio eliminado");
        }

    }
}
