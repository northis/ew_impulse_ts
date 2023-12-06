using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TrainBot.Root;

namespace TrainBot.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class WebHookController : ControllerBase
    {
        private readonly QueryHandler m_QueryHandler;

        public WebHookController(QueryHandler queryHandler)
        {
            m_QueryHandler = queryHandler;
        }

        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return new [] { "value1", "value2" };
        }

        // POST api/values
        [HttpPost]
        public async Task<IActionResult> Post(Update update)
        {
            if (update.Message != null)
                await m_QueryHandler.OnMessage(update.Message);

            if (update.CallbackQuery != null)
                await m_QueryHandler.CallbackQuery(update.CallbackQuery);

            if (update.InlineQuery != null)
                await m_QueryHandler.InlineQuery(update.InlineQuery);


            return Ok();
        }
    }
}
