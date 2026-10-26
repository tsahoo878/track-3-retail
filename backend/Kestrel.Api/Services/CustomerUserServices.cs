using FluentValidation;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Mappers;
using Kestrel.Api.Repositories;

namespace Kestrel.Api.Services;

public record CustomerSummaryDto(int TotalCustomers, int NewThisMonth);

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> SearchAsync(string? query, int page, int pageSize);
    Task<CustomerDto> GetAsync(int id);
    Task<CustomerDto> CreateAsync(SaveCustomerDto dto);
    Task<CustomerDto> UpdateAsync(int id, SaveCustomerDto dto);
    Task<CustomerSummaryDto> GetSummaryAsync();
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repo;
    private readonly IValidator<SaveCustomerDto> _validator;

    public CustomerService(ICustomerRepository repo, IValidator<SaveCustomerDto> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<PagedResult<CustomerDto>> SearchAsync(string? query, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _repo.SearchAsync(query, page, pageSize);
        return new PagedResult<CustomerDto>(items.Select(CustomerMapper.ToDto).ToList(), total, page, pageSize);
    }

    public async Task<CustomerDto> GetAsync(int id)
    {
        var c = await _repo.GetReadOnlyAsync(id) ?? throw new NotFoundException($"Customer {id} was not found.");
        return CustomerMapper.ToDto(c);
    }

    public async Task<CustomerDto> CreateAsync(SaveCustomerDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        if (await _repo.GetByEmailAsync(dto.Email.Trim().ToLowerInvariant()) != null)
            throw new ConflictException($"A customer with email {dto.Email} already exists.");
        var c = new Customer { CreatedUtc = DateTime.UtcNow };
        CustomerMapper.Apply(c, dto);
        await _repo.AddAsync(c);
        await _repo.SaveAsync();
        return CustomerMapper.ToDto(c);
    }

    public async Task<CustomerDto> UpdateAsync(int id, SaveCustomerDto dto)
    {
        await _validator.ValidateAndThrowAsync(dto);
        var c = await _repo.GetAsync(id) ?? throw new NotFoundException($"Customer {id} was not found.");
        CustomerMapper.Apply(c, dto);
        await _repo.SaveAsync();
        return CustomerMapper.ToDto(c);
    }

    public async Task<CustomerSummaryDto> GetSummaryAsync()
    {
        var all = await _repo.ListAllAsync();
        return new CustomerSummaryDto(all.Count, CountNewThisMonth(all));
    }

    public static int CountNewThisMonth(IEnumerable<Customer> customers)
    {
        var now = DateTime.UtcNow;
        return customers.Count(c => c.CreatedUtc.Year == now.Year && c.CreatedUtc.Month == now.Month);
    }
}

public interface IUserService
{
    Task<List<UserDto>> ListAsync();
    Task<UserDto> GetAsync(int id);
}

public class UserService : IUserService
{
    private readonly IUserRepository _repo;
    public UserService(IUserRepository repo) => _repo = repo;

    public async Task<List<UserDto>> ListAsync() => (await _repo.ListAsync()).Select(UserMapper.ToDto).ToList();

    public async Task<UserDto> GetAsync(int id)
    {
        var u = await _repo.GetAsync(id) ?? throw new NotFoundException($"User {id} was not found.");
        return UserMapper.ToDto(u);
    }
}
